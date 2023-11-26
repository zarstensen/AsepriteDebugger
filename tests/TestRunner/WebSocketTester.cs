using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace TestRunner
{
    /// <summary>
    /// 
    /// Base class for tests which require a web application which accept websocket client connections.
    /// 
    /// </summary>
    [Collection("Websockets")]
    public class WebSocketTester : IDisposable
    {
        /// <summary>
        /// Function called when a websocket connection is made on a route on the web app.
        /// </summary>
        public delegate Task WebsocketHandler(WebSocket webSocket);
        /// <summary>
        /// Comparer function for the wsAssert function.
        /// </summary>
        public delegate bool EqualFunc<T>(T? a, T? b);


        public readonly ITestOutputHelper output;

        /// <summary>
        /// Web application accepting websocket connections.
        /// </summary>
        public WebApplication? mock_websocket_server = null;
        /// <summary>
        /// token used to stop the mock_debug_adapter web application, when a test finishes or fails.
        /// </summary>
        public CancellationTokenSource web_app_token = new();
        /// <summary>
        /// String representing the current state of the server.
        /// Is mentioned in error messages generated from wsAssert*() functions.
        /// </summary>
        public string? server_state = null;

        // whether to wait for aseprite to close to complete the test, this can be turned on and off during a test.
        public bool fail_on_timeout = true;
        public WebSocketTester(ITestOutputHelper output) =>
            this.output = output;

        public void Dispose()
        {
            // force web application to shutdown, if it is currently up.
            if (mock_websocket_server != null && m_run_task != null && !web_app_token.IsCancellationRequested)
                wsForceClose().Wait();
        }

        /// <summary>
        /// 
        /// Create a webapplication which accepts websockets attempting to connect to the passed routes, at the passed endpoint.
        /// Each route should be passed a WebsocketHandler function, which is called when a websocket is connected to the server,
        /// at the specified route. When the WebsocketHandler exits, the websocket connection is closed.
        /// 
        /// </summary>
        public void wsCreate(Uri endpoint, Dictionary<string, WebsocketHandler> routes)
        {
            WebApplicationBuilder app_builder = WebApplication.CreateBuilder();
            app_builder.WebHost.UseUrls(endpoint.ToString());
            app_builder.Configuration.AddJsonFile("WebSocketTesterSettings.json");

            mock_websocket_server = app_builder.Build();
            mock_websocket_server.UseWebSockets();

            mock_websocket_server.MapGet("/", () => "Running");

            foreach (KeyValuePair<string, WebsocketHandler> route in routes)
            {
                mock_websocket_server.Map(route.Key, async context =>
                {
                    wsAssert(context.WebSockets.IsWebSocketRequest,
                        $"Incoming request was not a websocket for '{route.Key}' route");

                    using(WebSocket ws = await context.WebSockets.AcceptWebSocketAsync())
                    {
                        try
                        {
                            await route.Value.Invoke(ws);
                        }
                        catch(OperationCanceledException) { }
                        catch(Exception ex)
                        {
                            // if exception was caused by an wsAssert, we do not want to overwrite the assert message,
                            // so only do the assert if the websocket is not in a failed state.
                            if (!websocket_failed)
                                wsAssert(false, ex.ToString());
                        }

                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, web_app_token.Token);
                    }
                });
            }

        }

        /// <summary>
        /// Run the web application asynchronously.
        /// </summary>
        public void wsRun()
        {
            Assert.NotNull(mock_websocket_server);
            m_run_task = mock_websocket_server.RunAsync(web_app_token.Token);
        }

        /// <summary>
        /// Force the webapplication to shutdown.
        /// Also checks for any failed wsAsserts after server has shutdown.
        /// </summary>
        public async Task<bool> wsForceClose(bool assert = true)
        {
            Assert.NotNull(mock_websocket_server);
            web_app_token.Cancel();
            mock_websocket_server.WaitForShutdown();

            bool ws_failed = wsFailCheck(assert);

            Assert.NotNull(m_run_task);
            await m_run_task;

            return ws_failed;
        }

        /// <summary>
        /// 
        /// Wait for the webapplication to shutdown by itself, until the passed timeout has passed.
        /// After shutdown or timeout, wsAssert failures is checked, before it is checked if the shutdown process timed out.
        /// 
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns> whether a websocket has experienced a failure. </returns>
        public async Task<bool> wsWaitForClose(double timeout, bool assert = true)
        {
            Assert.NotNull(mock_websocket_server);

            bool timed_out = !await wsTimeoutTask(mock_websocket_server.WaitForShutdownAsync(web_app_token.Token), timeout);

            bool ws_fail = wsFailCheck(assert);

            if (timed_out)
            {
                if (assert && fail_on_timeout)
                    Assert.Fail($"Mock debug adapter was still running after timeout. ({timeout} s)\nStage:\t{server_state ?? "Unknown"}");

                return false;
            }

            Assert.NotNull(m_run_task);
            await m_run_task;

            return ws_fail;
        }

        /// <summary>
        /// Run the passed task until it finishes, or the timeout is passed.
        /// </summary>
        /// <param name="timeout"> timeout duration in seconds</param>
        /// <returns> whether the task did not timeout </returns>
        public async Task<bool> wsTimeoutTask(Task task, double timeout) =>
            await Task.WhenAny(task, Task.Delay((int)(timeout * 1000))) == task;

        /// <summary>
        /// Check for wsAssert failures on main test thread.
        /// </summary>
        /// <returns>whether the websocket has not failed</returns>
        public bool wsFailCheck(bool assert = true)
        {
            if (!websocket_failed)
                return true;

            if(assert)
                Assert.Fail(websocket_fail_message ?? "");

            return false;
        }

        /// <summary>
        /// Resets the websocket failed flag, which leads to wsFailCheck not failing for any failures before this function call.
        /// </summary>
        public void wsClearAssert() => websocket_failed = false;

        /// <summary>
        /// Simple assert method to be used inside the websocket handler.
        /// 
        /// as Assert functions do not work in a separate thread, we need to check if the websocket has failed in the test thread,
        /// which we do by setting a flag in the websocket thread if it fails, and checking this flag when testing has finished, in the test thread.
        /// </summary>
        /// <param name="condition"/>
        /// <param name="message"/>
        public void wsAssert(bool condition, string message, [CallerLineNumber] int line_number = 0)
        {
            if (!condition && !websocket_failed)
            {
                websocket_fail_message = $"Websocket Error:\n{message}\nLine: {line_number}";
                websocket_failed = true;
                web_app_token.Cancel();
            }
        }

        /// <summary>
        /// 
        /// Fails if expected and actual are not equal, and states their values in the error message.
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expected"></param>
        /// <param name="actual"></param>
        /// <param name="message"></param>
        public void wsAssertEq<T>(T expected, T? actual, string message = "", EqualFunc<T>? comparer = null, [CallerLineNumber] int line_number = 0)
        {
            comparer ??= EqualityComparer<T>.Default.Equals;
            wsAssert(comparer(expected, actual), $"{message}\nExpected:\t{expected}\nActual:\t{actual}", line_number);
        }

        // whether the websocket has failed a test.
        private bool websocket_failed = false;
        // error message of a failed websocket.
        private string? websocket_fail_message = null;
        private Task? m_run_task;
    }
}
