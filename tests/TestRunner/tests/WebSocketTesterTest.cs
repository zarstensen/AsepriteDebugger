using System.Net.WebSockets;
using TestRunner;
using Xunit.Abstractions;

namespace TestHelpers
{
    /// <summary>
    /// Tests for the WebSocketTester class, yes this is a test class for a test class.
    /// All tests which make use of the WebSocketTester should be in the Websockets collection,
    /// or make sure that their endpoint ports differ form all other tests.
    /// </summary>
    [Collection("Websockets")]
    public class WebSocketTesterTest : WebSocketTester
    {
        string endpoint = "127.0.0.1:8181";

        public WebSocketTesterTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task successfullAssert()
        {
            bool received = false;

            wsCreate(new($"http://{endpoint}"), new() {
                {
                    "/success_assert",
                    async ws =>
                    {
                        received = true;
                        wsAssert(true, "Should not fail");

                        // close web app
                        web_app_token.Cancel();
                    }
                }
            });
            Assert.NotNull(mock_websocket_server);

            wsRun();

            ClientWebSocket ws_client = new();
            Assert.True(await wsTimeoutTask(ws_client.ConnectAsync(new($"ws://{endpoint}/success_assert"), CancellationToken.None), 10));

            await wsWaitForClose(10);
            Assert.True(received);
        }

        [Fact]
        public async Task failedAssert()
        {
            bool received = false;

            wsCreate(new($"http://{endpoint}"), new() {
                {
                    "/failure_assert",
                    async ws =>
                    {
                        received = true;
                        wsAssert(false, "Should fail");

                        web_app_token.Cancel();
                    }
                }
            });

            Assert.NotNull(mock_websocket_server);

            wsRun();

            ClientWebSocket ws_client = new();
            Assert.True(await wsTimeoutTask(ws_client.ConnectAsync(new($"ws://{endpoint}/failure_assert"), CancellationToken.None), 10));

            Assert.False(await wsWaitForClose(10, assert: false));
            Assert.True(received);
        }

        [Fact]
        public async Task timeoutFailure()
        {
            bool received = false;

            wsCreate(new($"http://{endpoint}"), new() {
                {
                    "/timeout_failure",
                    async ws =>
                    {
                        received = true;
                        await Task.Delay(100000, web_app_token.Token);
                    }
                }
            });

            Assert.NotNull(mock_websocket_server);

            wsRun();

            ClientWebSocket ws_client = new();

            Assert.True(await wsTimeoutTask(ws_client.ConnectAsync(new($"ws://{endpoint}/timeout_failure"), CancellationToken.None), 10));

            Assert.False(await wsWaitForClose(1, assert: false));

            Assert.True(received);
        }

        [Fact]
        public async Task forceClose()
        {
            bool received = false;
            wsCreate(new($"http://{endpoint}"), new()
            {
                {
                    "/timeout_failure",
                    async ws => {
                        received = true;
                        while (!web_app_token.IsCancellationRequested);
                    }
                }
            });

            Assert.NotNull(mock_websocket_server);

            wsRun();

            ClientWebSocket ws_client = new();

            Assert.True(await wsTimeoutTask(ws_client.ConnectAsync(new($"ws://{endpoint}/timeout_failure"), CancellationToken.None), 10));
            Assert.True(await wsForceClose(assert: false));
            Assert.True(received);
        }
    }
}
