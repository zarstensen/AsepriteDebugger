using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using TestRunner;
using Xunit.Abstractions;

namespace Debugger
{
    /// <summary>
    /// 
    /// Tests for the aseprite debugger, using a mock debug adapter which sends requests to a debugger script running in an aseprite instance.
    /// 
    /// Each test should run the testAsepriteDebugger method, where the parameters passed to the method, defines the logic of the test.
    /// 
    /// </summary>
    [Collection("Websockets")]
    public class AsepriteDebuggerTest : WebSocketTester, IDisposable
    {
        delegate Task MockDebugAdapter(WebSocket socket);

        // endpoint to listen for debugger websocket connections on.
        const string ENDPOINT = "ws://127.0.0.1:8181";
        // endpoint route the websocket should connect to.
        const string DEBUGGER_ROUTE = "/ws";
        const string TEST_ROUTE = "/test_ws";
        const string EXT_NAME = "!AsepriteDebugger";



        // process object for the aseprite process running the test script.
        // is automatically closed when a test ends or fails.
        Process? aseprite_proc = null;

        // whether to use xvfb-run to run aseprite, or just run aseprite directly.
        // maps to ASEDEB_TEST_XVFB environment variable if set. 
        readonly bool use_xvfb = false;
        // location to store any logs produced by the test script running in aseprite.
        // maps to ASEDEB_SCRIPT_LOG if set.
        readonly string script_log = "./script_log.txt";
        // location of aseprite configuration directory.
        // maps ASEPRITE_USER_FOLDER, all tests fail if this variable is not set.
        readonly string aseprite_config_dir;

        // seq value to expect from the next debugger response or event, increments automatically.
        int debugger_seq = 1;
        // seq value to use for the next mock request, increments automatically.
        int client_seq = 1;


        public AsepriteDebuggerTest(ITestOutputHelper output)
            :base(output)
        {
            use_xvfb = (Environment.GetEnvironmentVariable("ASEDEB_TEST_XVFB")?.ToLower() ?? "false") == "true";
            script_log = Environment.GetEnvironmentVariable("ASEDEB_SCRIPT_LOG") ?? script_log;

            string? aseprite_user_folder = Environment.GetEnvironmentVariable("ASEPRITE_USER_FOLDER");

            Assert.True(aseprite_user_folder != null, "Tests cannot run, ASEPRITE_USER_FOLDER is not set!");

            aseprite_config_dir = aseprite_user_folder;
        }

        /// <summary>
        /// Close any open processes, and writes the script log to output.
        /// </summary>
        public new void Dispose()
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

                    // give aseprite 1 second to shutdown, otherwise force it to shutdown.
                    // this might happen if the test script has entered an indefinite loop.
                    if (!aseprite_proc?.WaitForExit(1000) ?? false)
                    {
                        aseprite_proc?.Kill();
                        // the log file is not instantly avaliable after a process kill, so have this sleep here to make sure it is.
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (InvalidOperationException) { }

            // write script log
            if (File.Exists(script_log))
            {
                output.WriteLine("\n========== SCRIPT LOG ==========\n");
                output.WriteLine(File.ReadAllText(script_log));
                File.Delete(script_log);
            }

            // remove debugger from aseprite

            if (Directory.Exists(Path.Combine(aseprite_config_dir, $"extensions/{EXT_NAME}")) && false)
                Directory.Delete(Path.Combine(aseprite_config_dir, $"extensions/{EXT_NAME}"), true);

            base.Dispose();
        }

        #region tests

        /// <summary>
        /// Test if LuaWebSocket send is working properly.
        /// Note: if LuaWebSocket is not working, the lua test wont be able to communicate with the test runner,
        /// so error messages might be a bit wired, but this should catch this sort of failure non the less.
        /// </summary>
        [Fact]
        public async Task sendWebSocketMessage() => await testAsepriteDebugger(timeout: 3, "send_message.lua", async ws =>
        {
            server_state = "Sending";

            await ws.SendAsync(
                Encoding.ASCII.GetBytes(File.ReadAllText("json/message_test/send_message.json")),
                WebSocketMessageType.Text,
                true,
                web_app_token.Token);

            server_state = "Waiting for close";
            Assert.Equal(WebSocketMessageType.Close, (await ws.ReceiveAsync(new byte[0], web_app_token.Token)).MessageType);
        });

        /// <summary>
        /// Test if LuaWebSocket receive is working properly
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task receiveWebSocketMessage() => await testAsepriteDebugger(timeout: 3, "receive_message.lua", async ws =>
        {
            server_state = "Receiving";

            JObject recv = await receiveWebsocketJson(ws);

            wsAssertEq("test_message", recv["type"]);

            server_state = "Waiting for close";
            Assert.Equal(WebSocketMessageType.Close, (await ws.ReceiveAsync(new byte[0], web_app_token.Token)).MessageType);
        });

        /// <summary>
        /// Test if the debugger responds correctly to an initialize request.
        /// </summary>
        [Fact]
        public async Task initializeDebugger() => await testAsepriteDebugger(timeout: 10, "initialize_test.lua", async ws =>
        {
            server_state = "Connected";
            JObject initialize_request = parseRequest("initialize_test/initialize_request.json");

            await sendWebsocketJson(ws, initialize_request);

            server_state = "Waiting for response";
            JObject response = await receiveWebsocketJson(ws);

            server_state = "Received response";
            JObject expected_response = parseResponse("initialize_test/initialize_response.json", initialize_request, true);

            wsAssertEq(expected_response, response, "Response did not match expected response.", comparer: JToken.DeepEquals);

            JObject expected_event = parseEvent("initialize_test/initialize_event.json");

            server_state = "Waiting for event";
            wsAssertEq(expected_event, await receiveWebsocketJson(ws), "Event did not match expected event.", comparer: JToken.DeepEquals);
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
            Assert.True(File.Exists(Path.Join("Debugger/tests", test_script)), $"Could not find test script: {Path.Join("Debugger/tests", test_script)}");

            runMockDebugAdapter(test_func);

            installDebugger(test_script);

            runAseprite();

            Assert.False(aseprite_proc.HasExited, $"Aseprite exited with code {(aseprite_proc.HasExited ? aseprite_proc.ExitCode : null)}");

            await wsWaitForClose(timeout);    
        }

        /// <summary>
        /// Starts a web application which listens for a websocket and redirects it to the passed test function.
        /// </summary>
        /// <param name="test_func"></param>
        private void runMockDebugAdapter(MockDebugAdapter test_func)
        {
            // the web application should only stop when both the test websocket and debugger websocket have exited,
            // as they might end up stopping the web app too early.
            bool should_stop = false;

            wsCreate(new(ENDPOINT.Replace("ws", "http")), new() {
                {
                    TEST_ROUTE,
                    async ws =>
                    {
                        JObject recv = await receiveWebsocketJson(ws);

                        while(true)
                            if (recv["type"]?.Value<string>() == "assert")
                            {
                                wsAssert(false, recv["message"]?.Value<string>() ?? "");
                                break;
                            }
                            else if (recv["type"]?.Value<string>() == "test_end")
                            {
                                if(should_stop)
                                    web_app_token.Cancel();

                                should_stop = true;

                                break;
                            }
                    }
                },
                {
                    DEBUGGER_ROUTE,
                    async ws =>
                    {
                        await test_func(ws);
                        
                        if(should_stop)
                            web_app_token.Cancel();

                        should_stop = true;
                    }
                }
            });

            wsRun();
        }


        /// <summary>
        /// Installs the debugger extension at ASEPRITE_USER_FOLDER, and configures it to run the passed test script in test mode.
        /// </summary>
        /// <param name="test_script"></param>
        private void installDebugger(string test_script)
        {
            // copy to extension directory.

            void copyDir(DirectoryInfo src_dir, DirectoryInfo dest_dir)
            {
                dest_dir.Create();

                foreach (FileInfo file in src_dir.GetFiles())
                    file.CopyTo(Path.Combine(dest_dir.FullName, file.Name), true);

                foreach (DirectoryInfo sub_dir in src_dir.GetDirectories())
                    copyDir(sub_dir, new DirectoryInfo(Path.Combine(dest_dir.FullName, sub_dir.Name)));
            }

            string dest_dir = Path.Combine(aseprite_config_dir, $"extensions/{EXT_NAME}");

            copyDir(new("Debugger"), new(dest_dir));

            JObject config = new();

            config["test_mode"] = true;
            config["endpoint"] = $"{ENDPOINT}{DEBUGGER_ROUTE}";
            config["test_endpoint"] = $"{ENDPOINT}{TEST_ROUTE}";
            config["test_script"] = new FileInfo($"Debugger/tests/{test_script}").FullName;
            config["log_file"] = new FileInfo(script_log).FullName;
            config["pipe_ws_path"] = new FileInfo("PipeWebSocket.exe").FullName;

            File.WriteAllText(Path.Combine(dest_dir, "config.json"), config.ToString());
        }

        /// <summary>
        /// 
        /// Start an aseprite process, where its output is redirected to test output.
        /// Uses xvfb-run is use_xvfb is true.
        /// 
        /// </summary>
        private void runAseprite()
        {
            aseprite_proc = new Process();

            aseprite_proc.StartInfo.FileName = use_xvfb ? "xvfb-run" : "aseprite";
            aseprite_proc.StartInfo.Arguments = use_xvfb ? "-e /dev/stdout -a aseprite" : "";
            aseprite_proc.StartInfo.RedirectStandardOutput = true;
            aseprite_proc.StartInfo.RedirectStandardError = true;
            aseprite_proc.StartInfo.RedirectStandardInput = true;
            aseprite_proc.StartInfo.UseShellExecute = false;
            aseprite_proc.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();

            aseprite_proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null && e.Data != string.Empty)
                    output.WriteLine($"ASE ERR: {e.Data}");
            };

            aseprite_proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null && e.Data != string.Empty)
                    output.WriteLine($"ASE OUT: {e.Data}");
            };

            Assert.True(aseprite_proc.Start());

            aseprite_proc.BeginErrorReadLine();
            aseprite_proc.BeginOutputReadLine();
        }

        /// <summary>
        /// Receives and returns the next relevant json message received form the debugger.
        /// </summary>
        private async Task<JObject> receiveWebsocketJson(WebSocket socket)
        {
            byte[] buffer = new byte[256];
            StringBuilder response_builder = new();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, web_app_token.Token);
                response_builder.Append(Encoding.ASCII.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            return JObject.Parse(response_builder.ToString());
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

        #endregion
    }

}