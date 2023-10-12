using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xunit.Abstractions;
using static AsepriteDebuggerTest.AsepriteDebuggerTest;

namespace AsepriteDebuggerTest
{
    /// <summary>
    /// 
    /// Tests for the aseprite debugger, using a mock debug adapter which sends requests to a debugger script running in an aseprite instance.
    /// 
    /// Each test should run the testAsepriteDebugger method, where the parameters passed to the method, defines the logic of the test.
    /// 
    /// </summary>
    public class AsepriteDebuggerTest : IDisposable
    {
        delegate Task MockDebugAdapter(WebSocket socket);
        
        // endpoint to listen for debugger websocket connections on.
        const string ENDPOINT = "http://127.0.0.1:8181";
        // endpoint route the websocket should connect to.
        const string DEBUGGER_ROUTE = "/ws";

        readonly ITestOutputHelper output;
        

        // process object for the aseprite process running the test script.
        // is automatically closed when a test ends or fails.
        Process? aseprite_proc = null;
        WebApplication? mock_debug_adapter = null;
        // token used to stop the mock_debug_adapter web application, when a test finishes or fails.
        CancellationTokenSource web_app_token = new();

        // whether to use xvfb-run to run aseprite, or just run aseprite directly.
        // maps to ASEDEB_TEST_XVFB environment variable if set. 
        readonly bool use_xvfb = false;
        // location to store any logs produced by the test script running in aseprite.
        // maps to ASEDEB_SCRIPT_LOG if set.
        readonly string script_log = "./script_log.txt";
        // location of lua scripts which can be passed to the testAsepriteDebugger method.
        // maps to ASEDEB_TEST_SCRIPT_DIR if set.
        readonly string test_src = "../../../../scripts";

        // whether the websocket has failed a test.
        bool websocket_failed = false;
        // error message of a failed websocket.
        string? websocket_fail_message = null;

        // seq value to expect from the next debugger response or event, increments automatically.
        int debugger_seq = 1;
        // seq value to use for the next mock request, increments automatically.
        int client_seq = 1;

        // brief message describing the current stage of the test.
        // will be printed in case of timeout, to clarify where exactly the test was stopped
        string? stage_msg = null;

        public AsepriteDebuggerTest(ITestOutputHelper output)
        {
            this.output = output;
            use_xvfb = (Environment.GetEnvironmentVariable("ASEDEB_TEST_XVFB")?.ToLower() ?? "false") == "true";
            script_log = Environment.GetEnvironmentVariable("ASEDEB_SCRIPT_LOG") ?? script_log;
            test_src = Environment.GetEnvironmentVariable("ASEDEB_TEST_SCRIPT_DIR") ?? test_src;
        }

        /// <summary>
        /// Close any open processes, and writes the script log to output.
        /// </summary>
        public void Dispose()
        {
            // close aseprite
            try
            {
                if (!aseprite_proc?.HasExited ?? false)
                {
                    if (use_xvfb)
                        aseprite_proc?.Close();
                    else
                        aseprite_proc?.CloseMainWindow();

                    aseprite_proc?.WaitForExit();
                }
            } catch (InvalidOperationException) { }

            // write script log
            if (File.Exists(script_log))
            {
                output.WriteLine("\n========== SCRIPT LOG ==========\n");
                output.WriteLine(File.ReadAllText(script_log));
                File.Delete(script_log);
            }

            // close mock debug adapter
            try
            {
                HttpClient client = new();
                HttpResponseMessage response = client.GetAsync(ENDPOINT).Result;

                web_app_token.Cancel();
                mock_debug_adapter?.WaitForShutdown();
            }
            catch (AggregateException e)
            {
                if (e.InnerException?.GetType() != typeof(HttpRequestException))
                    throw;
            }
        }

        #region tests

        /// <summary>
        /// Test if the debugger responds correctly to an initialize request.
        /// </summary>
        [Fact]
        public async Task InitializeTest() => await testAsepriteDebugger(timeout: 30, "initialize_test.lua", async ws =>
        {
            JObject initialize_request = parseRequest("initialize_test/initialize_request.json");

            await sendWebsocketJson(ws, initialize_request);

            stage_msg = "Waiting for response";
            JObject response = await recieveWebsocketJson(ws);

            JObject expected_response = parseResponse("initialize_test/initialize_response.json", initialize_request, true);

            wsAssertEq(expected_response, response, "Response did not match expected response.", comparer: JToken.DeepEquals);

            JObject expected_event = parseEvent("initialize_test/initialize_event.json");

            stage_msg = "Waiting for event";
            wsAssertEq(expected_event, await recieveWebsocketJson(ws), "Event did not match expected event.", comparer: JToken.DeepEquals);
        });

        #endregion

        #region helpers

        /// <summary>
        /// 
        /// Test a specific part of the aseprite debugger, using specific lua and c# test code.
        /// each testAsepriteDebugger call starts a new aseprite process, with the passed test script running,
        /// and a mock debug adapter, which runs the passed test function, when a websocket has connected.
        /// 
        /// Each call is limited to a specified timeout, before the test terminates and fails.
        /// 
        /// </summary>
        /// <param name="test_script"> lua script to run when starting aseprite. </param>
        /// <param name="timeout"> how long before test auto fails. </param>
        /// <param name="test_func"> c# function to run, when a websocket has connected to the mock debug adapter. </param>
        private async Task testAsepriteDebugger(double timeout, string test_script, MockDebugAdapter test_func)
        {
            // check that the test script exists.

            string test_main = Path.Join(test_src, "test_main.lua");

            Assert.True(File.Exists(test_main), $"Could not find main script: {test_main}");
            Assert.True(File.Exists(Path.Join(test_src, test_script)), $"Could not find test script: {Path.Join(test_src, test_script)}");

            // setup web app.

            WebApplicationBuilder app_builder = WebApplication.CreateBuilder();
            app_builder.WebHost.UseUrls(ENDPOINT);

            mock_debug_adapter = app_builder.Build();
            mock_debug_adapter.UseWebSockets();

            mock_debug_adapter.MapGet("/", () => "Running");

            mock_debug_adapter.Map(DEBUGGER_ROUTE, async context =>
            {
                wsAssert(context.WebSockets.IsWebSocketRequest, "Incomming request was not a websocket.");

                using (WebSocket web_socket = await context.WebSockets.AcceptWebSocketAsync())
                {
                    try
                    {
                        await test_func(web_socket);
                    }
                    catch (Exception ex)
                    {
                        // if exception was caused by an wsAssert, we do not want to overwrite the assert message,
                        // so only do the assert if the websocket is not in a failed state.
                        if(!websocket_failed)
                            wsAssert(false, ex.ToString());
                    }

                    web_app_token.Cancel();
                }
            });

            Task run_task = mock_debug_adapter.RunAsync(web_app_token.Token);

            output.WriteLine($"Running websocket at {ENDPOINT}{DEBUGGER_ROUTE}");

            // start aseprite with debugger.

            aseprite_proc = new Process();

            aseprite_proc.StartInfo.FileName = use_xvfb ? "xvfb-run" : "aseprite";
            aseprite_proc.StartInfo.Arguments = $"{(use_xvfb ? "aseprite" : "" )} --script-param debugger_endpoint=\"{ENDPOINT}{DEBUGGER_ROUTE}\" --script-param test_script=\"{test_script}\" --script-param debug_log_file=\"{script_log}\" --script \"{test_main}\"";
            aseprite_proc.StartInfo.RedirectStandardOutput = true;
            aseprite_proc.StartInfo.RedirectStandardError = true;
            aseprite_proc.StartInfo.RedirectStandardInput = true;
            aseprite_proc.StartInfo.UseShellExecute = false;
            aseprite_proc.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();

            aseprite_proc.ErrorDataReceived += (s, e) => {
                if (e.Data != null && e.Data != string.Empty)
                    output.WriteLine($"ASE ERR: {e.Data}");
            };
            
            aseprite_proc.OutputDataReceived += (s, e) => {
                if (e.Data != null && e.Data != string.Empty)
                    output.WriteLine($"ASE OUT: {e.Data}");
            };

            Assert.True(aseprite_proc.Start());

            aseprite_proc.BeginErrorReadLine();
            aseprite_proc.BeginOutputReadLine();

            // check if aseprite is still running or exited with error.

            Assert.False(aseprite_proc.HasExited, $"Aseprite exited with code {(aseprite_proc.HasExited ? aseprite_proc.ExitCode : null)}");

            // wait timeout

            mock_debug_adapter.WaitForShutdownAsync().Wait((int)(timeout * 1000));

            // check for websocket failures

            Assert.False(websocket_failed, websocket_fail_message);

            // check if web application is still running.

            try
            {
                HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(ENDPOINT);

                Assert.True(false, $"Mock debug adapter was still running after timeout. ({timeout} s)\nStage:\t{stage_msg ?? "Unknown"}");
            } catch(HttpRequestException) { }

            await run_task;
        }
        
        /// <summary>
        /// 
        /// Recieves and returns the next relevant json message recieved form the debugger.
        /// 
        /// This function also handles debugger test assert failed messages sent from the debugger,
        /// and will forward them to the current test, by wsAsserting.
        /// 
        /// </summary>
        private async Task<JObject> recieveWebsocketJson(WebSocket socket)
        {
            byte[] buffer = new byte[256];
            StringBuilder response_builder = new();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, web_app_token.Token);
                response_builder.Append(Encoding.ASCII.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            JObject json = JObject.Parse(response_builder.ToString());

            if (json["type"]?.Value<string>() == "assert")
                wsAssert(false, json["message"]?.Value<string>() ?? "Assertion failed in script");

            return json;
        }

        /// <summary>
        /// Send the passed JObject to the passed websocket in its string form, as a websocket text message.
        /// </summary>
        private async Task sendWebsocketJson(WebSocket socket, JObject json) =>
            await socket.SendAsync(Encoding.ASCII.GetBytes(json.ToString()), WebSocketMessageType.Text, true, web_app_token.Token);


        /// <summary>
        /// 
        /// Parse a Debug Adapter request json object.
        /// Parsed json objects type and seq field is automatically populated.
        /// 
        /// </summary>
        /// <param name="location"> location is relative to the json folder. </param>
        /// <param name="seq"> override seq parameter, also sets client_seq equal to seq + 1. </param>
        /// <returns></returns>
        private JObject parseRequest(string location, int? seq = null)
        {
            client_seq = seq ?? client_seq;

            JObject request = JObject.Parse(File.ReadAllText(Path.Join("json", location)));
            request["seq"] = client_seq++;
            request["type"] = "request";

            return request;
        }

        /// <summary>
        /// 
        /// Parse a debugger response json object.
        /// The json file should only contain the body of the response, as the returned json object,
        /// has the following fields automatically populated:
        ///     
        ///     seq
        ///     type
        ///     request_seq
        ///     command
        ///     message (optional)
        ///     success
        /// 
        /// </summary>
        /// <param name="location"> location is relative to the json folder. </param>
        /// <param name="request"> request json object to use when auto populating response fields. </param>
        /// <param name="success"> value of the success field. </param>
        /// <param name="short_err"> message fiedl value, should only be passed if success is false. </param>
        /// <param name="seq"> optionally override seq value. sets debugger_seq to seq + 1. </param>
        /// <returns></returns>
        private JObject parseResponse(string location, JObject request, bool success, string? short_err = null, int? seq = null)
        {
            debugger_seq = seq ?? debugger_seq;

            JObject response = new();
            
            response["seq"] = debugger_seq++;
            response["type"] = "response";
            
            response["request_seq"] = request["seq"]?.Value<int>();
            response["command"] = request["command"]?.Value<string>();
            response["success"] = success;

            response["body"] = JObject.Parse(File.ReadAllText(Path.Join("json", location)));

            if (short_err != null)
                response["message"] = short_err;

            return response;
        }

        private JObject parseEvent(string location)
        {
            JObject event_obj = JObject.Parse(File.ReadAllText(Path.Join("json", location)));

            event_obj["type"] = "event";
            event_obj["seq"] = debugger_seq++;
            
            return event_obj;
        }

        #region asserts

        /// <summary>
        /// Simple assert method to be used inside the websocket handler.
        /// 
        /// as Assert functions do not work in a separate thread, we need to check if the websocket has failed in the test thread,
        /// which we do by setting a flag in the websocket thread if it fails, and checking this flag when testing has finished, in the test thread.
        /// </summary>
        /// <param name="condition"/>
        /// <param name="message"/>
        private void wsAssert(bool condition, string message, [CallerLineNumber] int line_number = 0)
        {
            if(!condition)
            {
                websocket_fail_message = $"Websocket Error:\n{message}\nLine: {line_number}";
                websocket_failed = true;
                web_app_token.Cancel();
            }
        }

        delegate bool EqualFunc<T>(T? a, T? b);

        /// <summary>
        /// 
        /// Fails if expected and actual are not equal, and states their values in the error message.
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expected"></param>
        /// <param name="actual"></param>
        /// <param name="message"></param>
        private void wsAssertEq<T>(T expected, T? actual, string message, EqualFunc<T>? comparer = null, [CallerLineNumber] int line_number = 0)
        {
            comparer ??= EqualityComparer<T>.Default.Equals;
            wsAssert(comparer(expected, actual), $"{message}\nExpected:\t{expected}\nActual:\t{actual}", line_number);
        }

        #endregion
        #endregion
    }

}