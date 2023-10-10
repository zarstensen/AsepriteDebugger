using Microsoft.Extensions.Hosting;
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
    public class AsepriteDebuggerTest : IDisposable
    {
        const string endpoint = "http://127.0.0.1:8181";
        const string debugger_route = "/ws";
        const string test_route = "/test_ws";

        delegate Task MockDebugAdapter(WebSocket socket);

        readonly ITestOutputHelper output;
        Process? aseprite_proc = null;
        WebApplication? test_app = null;
        CancellationTokenSource app_token = new();

        bool has_failed = false;
        string? fail_message = null;

        public AsepriteDebuggerTest(ITestOutputHelper output) =>
            this.output = output;

        public void Dispose()
        {
            string script_log = Environment.GetEnvironmentVariable("ASEDEB_SCRIPT_LOG") ?? "";

            if (File.Exists(script_log))
            {
                output.WriteLine("SCRIPT LOG: ");
                output.WriteLine(File.ReadAllText(script_log));
                File.Delete(script_log);
            }

            if(!aseprite_proc?.HasExited ?? false)
                aseprite_proc?.Close();

            try
            {
                HttpClient client = new();
                HttpResponseMessage response = client.GetAsync(endpoint).Result;

                app_token.Cancel();
                test_app?.WaitForShutdown();
            }
            catch (AggregateException e)
            {
                if (e.InnerException?.GetType() != typeof(HttpRequestException))
                    throw;
            }
        }

        [Fact]
        public async Task Test1() => await testAsepriteDebugger("script.lua", timeout: 7, async ws =>
        {
            output.WriteLine("ATTEMPT RECIEVE");
            string response = await recieveWebsocketText(ws);
            output.WriteLine(response);

            wsAssert("Established Connection" == response, $"Invalid response: {response}");
            
        });

        [Fact]
        public async Task Test2() => await testAsepriteDebugger("script.lua", timeout: 7, async ws =>
        {
            output.WriteLine("ATTEMPT RECIEVE");
            string response = await recieveWebsocketText(ws);
            output.WriteLine(response);

            wsAssert("Established Connection".Length == response.Length, $"Invalid response: {response}");
        });

        private async Task testAsepriteDebugger(string test_script, double timeout, MockDebugAdapter test_func)
        {
            // check that the test script exists.

            string test_src = Environment.GetEnvironmentVariable("ASEDEB_TEST_SCRIPT_DIR") ?? "";

            string test_main = Path.Join(test_src, "test_main.lua");

            Assert.True(File.Exists(test_main), $"Could not find main script: {test_main}");
            Assert.True(File.Exists(Path.Join(test_src, test_script)), $"Could not find test script: {Path.Join(test_src, test_script)}");


            // setup web app.

            WebApplicationBuilder app_builder = WebApplication.CreateBuilder();
            app_builder.WebHost.UseUrls(endpoint);

            test_app = app_builder.Build();
            test_app.UseWebSockets();

            test_app.MapGet("/", () => "Running");

            test_app.Map(debugger_route, async context =>
            {
                output.WriteLine("CONNECT ATTEMPT");
                Assert.True(context.WebSockets.IsWebSocketRequest, "Incomming request was not a websocket.");

                using (WebSocket web_socket = await context.WebSockets.AcceptWebSocketAsync())
                {
                    await test_func(web_socket);
                }

                app_token.Cancel();
            });

            test_app.Map(test_route, async context =>
            {
                Assert.True(context.WebSockets.IsWebSocketRequest, "Incomming request was not a websocket.");

                using (WebSocket web_socket = await context.WebSockets.AcceptWebSocketAsync())
                    await web_socket.SendAsync(Encoding.ASCII.GetBytes(test_script), WebSocketMessageType.Text, true, CancellationToken.None);
            });

            Task run_task = test_app.RunAsync(app_token.Token);

            output.WriteLine($"Running websocket at {endpoint}{debugger_route}");

            // start aseprite with debugger.

            aseprite_proc = new Process();
            //     
            aseprite_proc.StartInfo.FileName = "xvfb-run";
            aseprite_proc.StartInfo.Arguments = $"aseprite --script-param debugger_endpoint=\"{endpoint}{debugger_route}\" --script-param test_script=\"{test_script}\" --script \"{test_main}\"";
            aseprite_proc.StartInfo.RedirectStandardOutput = true;
            aseprite_proc.StartInfo.RedirectStandardError = true;
            aseprite_proc.StartInfo.RedirectStandardInput = true;
            aseprite_proc.StartInfo.UseShellExecute = false;
            aseprite_proc.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            
            aseprite_proc.ErrorDataReceived += (s, e) => output.WriteLine($"ASE ERR: {e.Data}");
            aseprite_proc.OutputDataReceived += (s, e) => output.WriteLine($"ASE OUT: {e.Data}");

            Assert.True(aseprite_proc.Start());

            aseprite_proc.BeginErrorReadLine();
            aseprite_proc.BeginOutputReadLine();

            // check if aseprite is still running or exited with error.

            Assert.False(aseprite_proc.HasExited, $"Aseprite exited with code {(aseprite_proc.HasExited ? aseprite_proc.ExitCode : null)}");

            // wait timeout

            test_app.WaitForShutdownAsync().Wait((int)(timeout * 1000));

            // check for test failures

            Assert.False(has_failed, fail_message);

            // check if web application is still running.

            try
            {
                HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(endpoint);

                Assert.True(false, $"Mock debug adapter was still running after timeout. ({timeout} s)");
            } catch(HttpRequestException) { }

            await run_task;
        }
        private async Task<string> recieveWebsocketText(WebSocket socket)
        {
            byte[] buffer = new byte[256];
            StringBuilder response_builder = new();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                response_builder.Append(Encoding.ASCII.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            return response_builder.ToString();
        }

        private void wsAssert(bool condition, string message, [CallerLineNumber] int line_number = 0)
        {
            if(!condition)
            {
                fail_message = $"Websocket Error:\n{message}\nLine: {line_number}";
                has_failed = true;
                app_token.Cancel();
            }
        }
    }
}