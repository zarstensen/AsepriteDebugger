using System.Net;
using System.Net.Sockets;
using System.Text;
using PipeWebSocket;

namespace PipeWebSocket
{
    public class ProtocolTest
    {

        [Fact]
        public async Task send()
        {
            string msg = "message";
            MemoryStream stream = new();

            await Protocol.sendStream(msg, new(stream));

            Assert.Equal("!<LEN>\x07\x00\x00\x00!<MSG>message", Encoding.UTF8.GetString(stream.ToArray()));
        }

        [Fact]
        public async Task receive()
        {
            string valid_msg = "!<LEN>\x07\x00\x00\x00!<MSG>message";

            Assert.Equal("message", await Protocol.receiveStream(stringStreamReader(valid_msg)));

            string invalid_len_header = "<LEN>\x07\x00\x00\x00!<MSG>message";

            await Assert.ThrowsAsync<FormatException>(async () => await Protocol.receiveStream(stringStreamReader(invalid_len_header)));

            string invalid_msg_header = "!<LEN>\x07\x00\x00\x00<MSG>message";

            await Assert.ThrowsAsync<FormatException>(async () => await Protocol.receiveStream(stringStreamReader(invalid_msg_header)));
        }

        [Fact]
        public async Task tcpClientMultiMessage()
        {
            TcpClient source = new();
            TcpListener listener = new(IPAddress.Any, 0);

            listener.Start();

            Task connect_task = source.ConnectAsync(IPAddress.Loopback, (listener.LocalEndpoint as IPEndPoint).Port);
            TcpClient dest = await listener.AcceptTcpClientAsync();
            await connect_task;

            await Protocol.sendStream("A", new(source.GetStream()));
            await Protocol.sendStream("B", new(source.GetStream()));
            await Protocol.sendStream("C", new(source.GetStream()));

            // very important that it is the same StreamReader that is used every time, as it might skip messages otherwise.
            StreamReader reader = new(dest.GetStream());
            Assert.Equal("A", await Protocol.receiveStream(reader));
            Assert.Equal("B", await Protocol.receiveStream(reader));
            Assert.Equal("C", await Protocol.receiveStream(reader));

        }

        [Fact]
        public async Task exit()
        {
            MemoryStream stream = new();

            await Protocol.exitStream(new(stream));

            Assert.Equal(Protocol.EXIT_HEADER, Encoding.UTF8.GetString(stream.ToArray()));

            stream.Position = 0;

            Assert.Null(await Protocol.receiveStream(new(stream)));
        }

        private static StreamReader stringStreamReader(string str) =>
            new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(str)));
    }
}
