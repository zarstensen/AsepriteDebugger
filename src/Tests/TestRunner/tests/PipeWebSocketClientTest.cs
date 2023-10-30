using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Net.Sockets;

namespace PipeWebSocket
{
    public class PipeWebSocketClientTest
    {
        [Fact]
        public async Task pipeMessage()
        {
            // setup mock streams and tcp server.
            MemoryStream in_stream = new();

            TcpListener listener = new(IPAddress.Any, 0);

            PipeWebSocketClient client = new(input_stream: in_stream);

            listener.Start();
            await client.connect(IPAddress.Loopback, (listener.LocalEndpoint as IPEndPoint)!.Port);
            TcpClient tcp_client = await listener.AcceptTcpClientAsync();
            StreamReader tcp_reader = new(tcp_client.GetStream());

            // start messaging

            Task pipe_task = client.pipeData();

            await Protocol.sendStream("message", new(in_stream));
            in_stream.Position = 0;

            string? msg = await Protocol.receiveStream(tcp_reader);

            Assert.Equal("message", msg);

            // send exit messages.

            in_stream.SetLength(0);

            await Protocol.exitStream(new(in_stream));
            in_stream.Position = 0;

            await pipe_task;
        }
    }
}
