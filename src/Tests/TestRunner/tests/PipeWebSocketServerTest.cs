using PipeWebSocket;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using TestRunner;
using Xunit.Abstractions;

namespace PipeWebSocket
{
    /// <summary>
    /// Tests for the PipeWebSocketServer class.
    /// </summary>
    [Collection("Websockets")]
    public class PipeWebSocketServerTest : WebSocketTester
    {
        readonly Uri SERVER_ENDPOINT = new("http://127.0.0.1");
        readonly double TIMEOUT = 10; // seconds

        public PipeWebSocketServerTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task socketToWebsocket()
        {
            bool received = false;

            // setup connections.
            TcpClient socket_client = new();
            PipeWebSocketServer server = new();

            server.start();
            
            await wsTimeoutTask(
                Task.WhenAll(server.acceptPipeWebScoketClient(), socket_client.ConnectAsync(IPAddress.Loopback, server.Port)),
                10);

            wsCreate(SERVER_ENDPOINT, new() {
                {
                    "/ws",
                    async ws =>
                    {
                        string res = await Protocol.receiveWebsocket(ws);
                        Assert.Equal("message", res);
                        received = true;

                        // exit message should come here
                        Assert.Null(await Protocol.receiveWebsocket(ws));
                    }
                }
            });
            wsRun();
            await server.connectWebsocket(new(SERVER_ENDPOINT.ToString().Replace("http", "ws") + "ws"));

            // actually send messages
            Task forward_task = server.forwardMessages();

            await Protocol.sendStream("message", new(socket_client.GetStream()));
            await Protocol.exitStream(new(socket_client.GetStream()));
            
            await wsWaitForClose(TIMEOUT);

            await wsTimeoutTask(forward_task, TIMEOUT);
            server.stop();

            // make sure message was actually passed to the websocket.
            Assert.True(received);
        }

        [Fact]
        public async Task websocketToPipe()
        {
            // setup connections
            MemoryStream out_stream = new();
            TcpClient socket_client = new();
            PipeWebSocketServer server = new(output_stream: out_stream);

            server.start();
            await Task.WhenAll(server.acceptPipeWebScoketClient(), socket_client.ConnectAsync(IPAddress.Loopback, server.Port));

            wsCreate(SERVER_ENDPOINT, new()
            {
                {
                    "/ws",
                    async ws =>
                    {
                        await ws.SendAsync(Encoding.UTF8.GetBytes("message"), WebSocketMessageType.Text, true, web_app_token.Token);
                        Assert.Null(await Protocol.receiveWebsocket(ws));
                    }
                }
            });
            wsRun();
            await server.connectWebsocket(new(SERVER_ENDPOINT.ToString().Replace("http", "ws") + "ws"));

            Task forward_task = server.forwardMessages();

            // wait for output stream to be populated.
            // this should not be a problem with stdin, as read calls are blocking if the stream is empty,
            // and its position is also at the unread data, instead of the end of stdin.
            while(out_stream.Length <= 0) { await Task.Delay(25); }
            out_stream.Position = 0;

            Assert.Equal("message", await Protocol.receiveStream(new(out_stream)));
            await Protocol.exitStream(new(socket_client.GetStream()));

            await wsWaitForClose(TIMEOUT);

            await wsTimeoutTask(forward_task, TIMEOUT);
            server.stop();
        }

        [Fact]
        public async Task serverForceClose()
        {
            // setup connections.
            TcpClient socket_client = new();
            PipeWebSocketServer server = new();

            server.start();

            await wsTimeoutTask(
                Task.WhenAll(server.acceptPipeWebScoketClient(), socket_client.ConnectAsync(IPAddress.Loopback, server.Port)),
                TIMEOUT);

            wsCreate(SERVER_ENDPOINT, new() {
                {
                    "/ws",
                    // websocket is closed without an exit signal from socket_client.
                    async ws => { }
                }
            });
            wsRun();
            await server.connectWebsocket(new(SERVER_ENDPOINT.ToString().Replace("http", "ws") + "ws"));

            // actually send messages
            Task forward_task = server.forwardMessages();

            await wsWaitForClose(TIMEOUT);

            await wsTimeoutTask(Assert.ThrowsAsync<OperationCanceledException>(() => forward_task), TIMEOUT);
            server.stop();
        }
    }
}
