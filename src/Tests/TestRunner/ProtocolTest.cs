using System.Text;
using WSPipeServer;

namespace PipeWebsocketTest
{
    public class ProtocolTest
    {

        [Fact]
        public void send()
        {
            string msg = "message";
            MemoryStream stream = new();

            Protocol.send(msg, new(stream));

            Assert.Equal("!<LEN>\x07\x00\x00\x00!<MSG>message", Encoding.UTF8.GetString(stream.ToArray()));
        }

        [Fact]
        public void receive()
        {
            string valid_msg = "!<LEN>\x07\x00\x00\x00!<MSG>message";
            
            Assert.Equal("message", Protocol.receive(stringStreamReader(valid_msg)));

            string invalid_len_header = "<LEN>\x07\x00\x00\x00!<MSG>message";

            Assert.Null(Protocol.receive(stringStreamReader(invalid_len_header)));

            string invalid_msg_header = "!<LEN>\x07\x00\x00\x00<MSG>message";

            Assert.Null(Protocol.receive(stringStreamReader(invalid_msg_header)));
        }

        [Fact]
        public void exit()
        {
            bool client_exited = false;
            bool server_exited = false;
            MemoryStream stream = new();

            Action client_exit_event = () => client_exited = true;
            Action server_exit_event = () => server_exited = true;

            Protocol.OnExit += client_exit_event;
            Protocol.exit(new(stream));

            Assert.Equal(Protocol.EXIT_HEADER, Encoding.UTF8.GetString(stream.ToArray()));
            Assert.True(client_exited);

            Protocol.OnExit -= client_exit_event;
            Protocol.OnExit += server_exit_event;

            stream.Position = 0;

            Assert.Null(Protocol.receive(new(stream)));
            Assert.True(server_exited);
        }

        public static StreamReader stringStreamReader(string str) =>
            new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(str)));
    }
}
