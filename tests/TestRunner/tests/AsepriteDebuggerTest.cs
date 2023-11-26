using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
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
            : base(output)
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

            if (Directory.Exists(Path.Combine(aseprite_config_dir, $"extensions/{EXT_NAME}")))
                Directory.Delete(Path.Combine(aseprite_config_dir, $"extensions/{EXT_NAME}"), true);

            base.Dispose();
        }

        #region tests

        /// <summary>
        /// Test if LuaWebSocket send is working properly.
        /// Note: if LuaWebSocket is not working, the lua test wont be able to communicate with the test runner,
        /// so error messages might be a bit weird, but this should catch this sort of failure non the less.
        /// </summary>
        [Fact]
        public async Task sendWebSocketMessage() => await testAsepriteDebugger(timeout: 30, "send_message.lua", async ws =>
        {
            server_state = "Sending";

            await ws.SendAsync(
                Encoding.ASCII.GetBytes(File.ReadAllText("json/message_test/send_message.json")),
                WebSocketMessageType.Text,
                true,
                web_app_token.Token);

            server_state = "Waiting for close";
            wsAssertEq(WebSocketMessageType.Close, (await ws.ReceiveAsync(new byte[0], web_app_token.Token)).MessageType);
        });

        /// <summary>
        /// Test if LuaWebSocket receive is working properly
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task receiveWebSocketMessage() => await testAsepriteDebugger(timeout: 30, "receive_message.lua", async ws =>
        {
            server_state = "Receiving";

            JObject recv = await receiveWebsocketJson(ws);

            wsAssertEq("test_message", recv["type"]);

            server_state = "Waiting for close";
            wsAssertEq(WebSocketMessageType.Close, (await ws.ReceiveAsync(new byte[0], web_app_token.Token)).MessageType);
        });

        [Fact]
        public async Task logMessageToClient() => await testAsepriteDebugger(timeout: 30, "log_message.lua", no_websocket_logging: false, test_func: async ws =>
        {
            server_state = "Connected";

            await sendWebsocketJson(ws, parseRequest("initialize_request.json"));
            await receiveNextResponse(ws, "initialize");
            await receiveNextEvent(ws, "initialized");

            await sendWebsocketJson(ws, parseRequest("launch_request.json"));
            await receiveNextResponse(ws, "launch");

            await sendWebsocketJson(ws, parseRequest("configdone_request.json"));
            await receiveNextResponse(ws, "configurationDone");

            int n_receive = 1;

            
            while (!web_app_token.IsCancellationRequested)
            {
                // other log events might come here aswell, so we just ignore those and block until we get the correct message.
                JObject log_event = await receiveNextEvent(ws, "output", $"Receive [{n_receive++}]");

                if (log_event["body"]?.Value<string>("output") == "!<TEST LOG MESSAGE>!\t")
                {
                    break;
                }
            }

        });

        [Fact]
        public async Task terminateEvent() => await testAsepriteDebugger(timeout: 30, "terminate_test.lua", async ws =>
        {
            await beginInitializeDebugger(ws);
            await endInitializeDebugger(ws);

            // previous version of this test shutdown aseprite here,
            // however this does not call the debugger extensions exit function when run with xvfb,
            // as an alternative, the debugger is simply deinitted manually.

            await receiveNextEvent(ws, "terminated");
        });

        /// <summary>
        /// Test if breakpoints are set and hit correctly.
        /// </summary>
        [Fact]
        public async Task settingAndHittingBreakpoints() => await testAsepriteDebugger(timeout: 30, "breakpoints_test.lua", async ws =>
        {
            await beginInitializeDebugger(ws);
            
            // set breakpoints

            JObject breakpoints_response = await setBreakpoints(ws, "breakpoints_test.lua", new List<int> { 8, 11, 18 });

            // check breakpoints were set correctly

            wsAssertEq(3, breakpoints_response["body"]?["breakpoints"]?.Count(), "Breakpoint count did not match.");

            wsAssertEq(8, breakpoints_response["body"]?["breakpoints"]?[0]?["line"], "Breakpoint placement was unexpected.");
            wsAssertEq(11, breakpoints_response["body"]?["breakpoints"]?[1]?["line"], "Breakpoint placement was unexpected.");
            wsAssertEq(18, breakpoints_response["body"]?["breakpoints"]?[2]?["line"], "Breakpoint placement was unexpected.");

            await endInitializeDebugger(ws);

            // check breakpoints are hit

            for (int i = 0; i < 3; i++)
            {
                server_state = $"Waiting for stop event ";
                await receiveNextEvent(ws, "stopped", state_msg: $"nr. [{i + 1}]");
                await sendWebsocketJson(ws, parseRequest("continue_request.json"));
                await receiveNextResponse(ws, "continue");
            }
        });

        [Fact]
        public async Task evaluateExpression() => await testAsepriteDebugger(timeout: 30, "evaluate_test.lua", async ws =>
        {
            await beginInitializeDebugger(ws);

            await setBreakpoints(ws, "evaluate_test.lua", new List<int> { 6 });

            await endInitializeDebugger(ws);

            await receiveNextEvent(ws, "stopped");

            await sendWebsocketJson(ws, parseRequest("evaluate_test/evaluate_request.json"));
            wsAssertEq(2, int.Parse((await receiveNextResponse(ws, "evaluate"))["body"]?.Value<string>("result") ?? "-1"), "Evaluation failed.");

            await sendWebsocketJson(ws, parseRequest("evaluate_test/evaluate_request_fail.json"));
            wsAssertEq(false, (await receiveNextResponse(ws, "evaluate", false)).Value<bool>("success"), "Evaluation did not fail.");

            await sendWebsocketJson(ws, parseRequest("continue_request.json"));
            await receiveNextResponse(ws, "continue");

        });

        /// <summary>
        /// test retreival of various variable types from various scopes.
        /// </summary>
        [Fact]
        public async Task retreivingVariables() => await testAsepriteDebugger(timeout: 30, "variables_test.lua", async ws =>
        {
            await beginInitializeDebugger(ws);

            await setBreakpoints(ws, "variables_test.lua", new List<int> { 15 });

            await endInitializeDebugger(ws);

            await receiveNextEvent(ws, "stopped");

            JObject threads_request = parseRequest("threads_request.json");
            await sendWebsocketJson(ws, threads_request);
            wsAssertEq(
                parseResponse("variables_test/threads_response.json", threads_request, true),
                await receiveNextResponse(ws, "threads"),
                comparer: JToken.DeepEquals);

            JObject stacktrace_request = parseRequest("stacktrace_request.json");
            await sendWebsocketJson(ws, stacktrace_request);
            JObject stacktrace_response = await receiveNextResponse(ws, "stackTrace");


            JObject scopes_request = parseRequest("variables_test/scopes_request.json");
            await sendWebsocketJson(ws, scopes_request);
            JObject scopes_response = await receiveNextResponse(ws, "scopes");

            wsAssertEq("scopes", scopes_response.Value<string>("command"), "Received incorrect response.");
            wsAssertEq(6, scopes_response["body"]?["scopes"]?.Count(), "Received invalid amount of scopes.");

            wsAssertEq(
                6,
                scopes_response["body"]?["scopes"]?
                    .Select(scope => scope.Value<int>("variablesReference"))
                    .Distinct()
                    .Count(),
                "All variablesReference values were not unique.");

            JObject variables_request = parseRequest("variables_test/variables_request.json");
            int variables_reference = 1;

            // locals
            
            variables_request["arguments"]!["variablesReference"] = variables_reference++;

            await sendWebsocketJson(ws, variables_request);
            JObject locals_response = await receiveNextResponse(ws, "variables");

            wsAssert(locals_response["body"]?["variables"]?.Where(variable =>
            variable.Value<string>("name") == "local_number"
            && variable.Value<int>("value") == 1).Count() == 1, "local_number variable not found.");

            List<JToken>? table_var = locals_response["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == "local_list_table"
                && variable.Value<string>("type") == "table")?.ToList();

            wsAssert(table_var?.Count == 1, "local_list_table variable not found");


            List<JToken>? aseprite_rect_var = locals_response["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == "local_aseprite_rect"
                && variable.Value<string>("type") == "userdata")?.ToList();

            wsAssert(aseprite_rect_var?.Count == 1, "local_aseprite_rect variable not found");

            // locals table

            variables_request["arguments"]!["variablesReference"] = table_var![0].Value<int>("variablesReference");
            await sendWebsocketJson(ws, variables_request);
            JObject table_response = await receiveNextResponse(ws, "variables");

            wsAssertEq(4, table_response["body"]?["variables"]?.Count(), "Invalid table fields retreived");

            wsAssert(table_response["body"]?["variables"]?.Where(variable =>
            variable.Value<string>("name")?.Contains('1') ?? false
            && variable.Value<string>("value") == "a").Count() == 1, "Could not find table list element 1");

            wsAssert(table_response["body"]?["variables"]?.Where(variable =>
            variable.Value<string>("name")?.Contains('2') ?? false
            && variable.Value<string>("value") == "b").Count() == 1, "Could not find table list element 2");

            wsAssert(table_response["body"]?["variables"]?.Where(variable =>
            variable.Value<string>("name") == "a"
            && variable.Value<int>("value") == 1).Count() == 1, "Could not find table kv pair");

            wsAssert(table_response["body"]?["variables"]?.Where(variable =>
            variable.Value<string>("name") == "b"
            && variable.Value<int>("value") == 2).Count() == 1, "Could not find table kv pair");

            // locals aseprite rect

            variables_request["arguments"]!["variablesReference"] = aseprite_rect_var![0].Value<int>("variablesReference");
            await sendWebsocketJson(ws, variables_request);
            JObject aseprite_rect_resposne = await receiveNextResponse(ws, "variables");

            foreach (var kv_pair in new Dictionary<string, int> { { "x", 10 }, { "y", 20 }, { "w", 30 }, { "h", 40 } })
            {
                wsAssert(aseprite_rect_resposne["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == kv_pair.Key
                && variable.Value<int>("value") == kv_pair.Value).Count() == 1, $"Invalid '{kv_pair.Key}'");
            }

            // arguments
            variables_request["arguments"]!["variablesReference"] = variables_reference++;
            await sendWebsocketJson(ws, variables_request);
            JObject arguments_response = await receiveNextResponse(ws, "variables");

            foreach (var kv_pair in new Dictionary<string, string> { { "arg_1", "arg_1" }, { "arg_2", "arg_2" }, { "(vararg)", "vararg" } })
            {
                wsAssert(arguments_response["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == kv_pair.Key
                && variable.Value<string>("value") == kv_pair.Value).Count() == 1, $"Invalid '{kv_pair.Key}'");
            }

            // upvalues
            variables_request["arguments"]!["variablesReference"] = variables_reference++;
            await sendWebsocketJson(ws, variables_request);
            JObject upvalues_response = await receiveNextResponse(ws, "variables");

            wsAssert(upvalues_response["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == "up_value"
                && variable.Value<bool>("value") == true).Count() == 1, $"Invalid upvalue value");

            // globals

            variables_request["arguments"]!["variablesReference"] = variables_reference++;
            await sendWebsocketJson(ws, variables_request);
            JObject globals_response = await receiveNextResponse(ws, "variables");

            wsAssert(globals_response["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == "Global"
                && variable.Value<string>("value") == "Global").Count() == 1, $"Invalid globals value");

            // globals default

            variables_request["arguments"]!["variablesReference"] = variables_reference++;
            await sendWebsocketJson(ws, variables_request);
            JObject globals_default_response = await receiveNextResponse(ws, "variables");

            // the '_G' variable should always be part of the default global scope.
            wsAssert(globals_default_response["body"]?["variables"]?.Where(variable =>
                variable.Value<string>("name") == "_G").Count() == 1, $"Invalid default globals value");

            await sendWebsocketJson(ws, parseRequest("continue_request.json"));
            await receiveNextResponse(ws, "continue");

        });

        [Fact]
        public async Task codeStepping() => await testAsepriteDebugger(timeout: 30, "code_stepping.lua", async ws =>
        {
            await beginInitializeDebugger(ws);
            await setBreakpoints(ws, "code_stepping.lua", new List<int> { 28, 21 });
            await endInitializeDebugger(ws);

            await receiveNextEvent(ws, "stopped");

            await sendWebsocketJson(ws, parseRequest("code_stepping/step_in_request.json"));
            await receiveNextResponse(ws, "stepIn");

            wsAssertEq("step", (await receiveNextEvent(ws, "stopped"))?["body"]?.Value<string>("reason"));

            await sendWebsocketJson(ws, parseRequest("stacktrace_request.json"));
            wsAssertEq(14, 
                (await receiveNextResponse(ws, "stackTrace"))?["body"]?["stackFrames"]?[0]
                ?.Value<int>("line"));

            await sendWebsocketJson(ws, parseRequest("code_stepping/step_out_request.json"));
            await receiveNextResponse(ws, "stepOut");

            wsAssertEq("step", (await receiveNextEvent(ws, "stopped"))?["body"]?.Value<string>("reason"));

            await sendWebsocketJson(ws, parseRequest("stacktrace_request.json"));
            wsAssertEq(29,
                (await receiveNextResponse(ws, "stackTrace"))?["body"]?["stackFrames"]?[0]
                ?.Value<int>("line"));

            await sendWebsocketJson(ws, parseRequest("code_stepping/next_request.json"));
            await receiveNextResponse(ws, "next");

            wsAssertEq("step", (await receiveNextEvent(ws, "stopped"))?["body"]?.Value<string>("reason"));

            await sendWebsocketJson(ws, parseRequest("stacktrace_request.json"));
            wsAssertEq(30,
                (await receiveNextResponse(ws, "stackTrace"))?["body"]?["stackFrames"]?[0]
                ?.Value<int>("line"));

            await sendWebsocketJson(ws, parseRequest("continue_request.json"));
            await receiveNextResponse(ws, "continue");

            // tail calls

            await receiveNextEvent(ws, "stopped");

            await sendWebsocketJson(ws, parseRequest("code_stepping/step_out_request.json"));
            await receiveNextResponse(ws, "stepOut");

            wsAssertEq("step", (await receiveNextEvent(ws, "stopped"))?["body"]?.Value<string>("reason"));

            await sendWebsocketJson(ws, parseRequest("stacktrace_request.json"));
            wsAssertEq(31,
                (await receiveNextResponse(ws, "stackTrace"))?["body"]?["stackFrames"]?[0]
                ?.Value<int>("line"));

            // pcall

            await sendWebsocketJson(ws, parseRequest("code_stepping/next_request.json"));
            await receiveNextResponse(ws, "next");

            wsAssertEq("step", (await receiveNextEvent(ws, "stopped"))?["body"]?.Value<string>("reason"));

            await sendWebsocketJson(ws, parseRequest("stacktrace_request.json"));
            wsAssertEq(32,
                (await receiveNextResponse(ws, "stackTrace"))?["body"]?["stackFrames"]?[0]
                ?.Value<int>("line"));

            await sendWebsocketJson(ws, parseRequest("continue_request.json"));
            await receiveNextResponse(ws, "continue");

        });

        [Fact]
        public async Task errorHandling() => await testAsepriteDebugger(timeout: 5, "error_test.lua", report_errors: false, test_func: async ws =>
        {
            await beginInitializeDebugger(ws);
            await endInitializeDebugger(ws);

            JObject json_event = await receiveNextEvent(ws, "stopped");

            wsAssertEq("exception", json_event?["body"]?.Value<string>("reason"));
            wsAssertEq("error", json_event?["body"]?.Value<string>("description"));

            await sendWebsocketJson(ws, parseRequest("error_test/exception_info_request.json"));
            JObject exception_info_response = await receiveNextResponse(ws, "exceptionInfo");

            wsAssertEq("error", exception_info_response?["body"]?.Value<string>("exceptionId"));

            await sendWebsocketJson(ws, parseRequest("continue_request.json"));
            await receiveNextResponse(ws, "continue");

            await receiveNextEvent(ws, "terminated");
            fail_on_timeout = false;
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
        private async Task testAsepriteDebugger(double timeout, string test_script, MockDebugAdapter test_func, bool no_websocket_logging = true, bool report_errors = true)
        {
            Assert.True(File.Exists(Path.Join("Debugger/tests", test_script)), $"Could not find test script: {Path.Join("Debugger/tests", test_script)}");

            runMockDebugAdapter(test_func);

            installDebugger(test_script, no_websocket_logging, report_errors);

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

                        await sendWebsocketJson(ws, JObject.Parse("""{ "type": "test_end" }"""));
                        
                        if(should_stop)
                            web_app_token.Cancel();

                        should_stop = true;

                        while(!web_app_token.IsCancellationRequested)
                            await Task.Delay(50);
                    }
                }
            });

            wsRun();
        }


        /// <summary>
        /// Installs the debugger extension at ASEPRITE_USER_FOLDER, and configures it to run the passed test script in test mode.
        /// </summary>
        /// <param name="test_script"></param>
        private void installDebugger(string test_script, bool websocket_logging, bool report_errors)
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
            config["no_websocket_logging"] = websocket_logging;
            config["endpoint"] = $"{ENDPOINT}{DEBUGGER_ROUTE}";
            config["test_endpoint"] = $"{ENDPOINT}{TEST_ROUTE}";
            config["test_script"] = new FileInfo($"Debugger/tests/{test_script}").FullName;
            config["log_file"] = new FileInfo(script_log).FullName;
            config["pipe_ws_path"] = new FileInfo("PipeWebSocket.exe").FullName;
            // since the scrit is run using the --script command line option, the source and install path will be equal.
            config["install_dir"] = config["source_dir"] = new DirectoryInfo($"Debugger/tests/").FullName;
            config["report_errors"] = report_errors;

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
        /// Receives the next message of passed type, discarding any other message types received.
        /// </summary>
        private async Task<JObject> receiveNextResponse(WebSocket socket, string? expected_command = null, bool assert_on_failed = true, string? state_msg = null, [CallerLineNumber] int line_number = 0)
        {
            server_state = $"Waiting on '{expected_command}' response\nLine: {line_number}";

            if (state_msg != null)
                server_state = $"{server_state}: {state_msg}";

            JObject response;

            do
            {
                response = await receiveWebsocketJson(socket);
            } while (response.Value<string>("type") != "response");

            server_state = $"Received '{expected_command}' response\nLine: {line_number}";

            if (state_msg != null)
                server_state = $"{server_state}\n{state_msg}";

            if (assert_on_failed)
                wsAssert(response.Value<bool>("success"), $"Did not receive successfull response:\n{response}", line_number: line_number);

            if (expected_command != null)
                wsAssertEq(expected_command, response.Value<string>("command"), $"Received unexpected command type:\n{response}", line_number: line_number);

            return response;
        }

        private async Task<JObject> receiveNextEvent(WebSocket socket, string? expected_event = null, string? state_msg = null, [CallerLineNumber] int line_number = 0)
        {
            server_state = $"Waiting on '{expected_event}' event\nLine: {line_number}";

            if (state_msg != null)
                server_state = $"{server_state}\n{state_msg}";

            JObject event_message;

            do
            {
                event_message = await receiveWebsocketJson(socket);
            } while (event_message.Value<string>("type") != "event" || event_message.Value<string>("event") == "stackTraceUpdate");

            server_state = $"Received '{expected_event}' event\nLine: {line_number}";

            if (state_msg != null)
                server_state = $"{server_state}\n{state_msg}";

            if (expected_event != null)
                wsAssertEq(expected_event, event_message.Value<string>("event"), $"Received unexpected event type:\n{event_message}", line_number: line_number);

            return event_message;
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
        private JObject parseResponse(string location, JObject request, bool success, string? short_err = null)
        {
            JObject response = new();

            response["seq"] = 0;
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

        private async Task beginInitializeDebugger(WebSocket ws)
        {
            JObject initialize_request = parseRequest("initialize_request.json");

            await sendWebsocketJson(ws, initialize_request);

            JObject response = await receiveNextResponse(ws, "initialize");

            // TODO: should probably not test the exact capabilities here?
            JObject expected_response = parseResponse("initialize_test/initialize_response.json", initialize_request, true);

            wsAssertEq(expected_response, response, "Response did not match expected response.", comparer: JToken.DeepEquals);

            await receiveNextEvent(ws, "initialized");

            await sendWebsocketJson(ws, parseRequest("launch_request.json"));
            await receiveNextResponse(ws, "launch");
        }

        private async Task endInitializeDebugger(WebSocket ws)
        {
            await sendWebsocketJson(ws, parseRequest("configdone_request.json"));
            await receiveNextResponse(ws, "configurationDone");
        }

        private async Task<JObject> setBreakpoints(WebSocket ws, string script, List<int> lines)
        {
            JObject breakpoints_request = parseRequest("set_breakpoints_request.json");

            // source path needs to be absolute path, so it needs to be set in code.
            breakpoints_request["arguments"]!["source"] = JObject.Parse($"{{ \"path\": \"{new FileInfo($"Debugger/tests/{script}").FullName.Replace(@"\", @"\\")}\" }}");
            
            foreach(int line in lines)
                breakpoints_request["arguments"]!.Value<JArray>("breakpoints")!.Add(JObject.Parse($"{{ \"line\": {line} }}"));

            await sendWebsocketJson(ws, breakpoints_request);
            return await receiveNextResponse(ws, "setBreakpoints");
        }

        #endregion
    }

}