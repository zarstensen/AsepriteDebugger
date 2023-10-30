using System.Net;
using System.Net.Sockets;

namespace PipeWebSocket
{
    /// <summary>
    /// Receives data from a stream (stdin by default) and pipes it to a PipeWebSocketServer.
    /// </summary>
    public class PipeWebSocketClient
    {

        public PipeWebSocketClient(Stream? input_stream = null)
        {
            m_input_stream = new StreamReader(input_stream ?? Console.OpenStandardInput());
        }

        public async Task connect(IPAddress address, int port)
        {
            await m_client.ConnectAsync(address, port);

            m_client_writer = new StreamWriter(m_client.GetStream());
        }

        public async Task pipeData()
        {
            if (m_client_writer == null)
                throw new InvalidOperationException("A tcp client must be connected.");

            await Task.Run(async () => await forwardStream(m_input_stream, m_client_writer, m_forward_token.Token));
        }

        private async Task forwardStream(StreamReader in_stream, StreamWriter out_stream, CancellationToken token = default)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    string? msg;

                    try
                    {
                        msg = await Protocol.receiveStream(in_stream, token);
                    }
                    catch (FormatException)
                    {
                        await Task.Delay(50);
                        continue;
                    }

                    if (msg == null)
                    {
                        await Protocol.exitStream(out_stream);
                        break;
                    }

                    await Protocol.sendStream(msg!, out_stream);
                }
            }
            catch(Exception)
            {
                m_forward_token.Cancel();
                throw;
            }
        }

        private TcpClient m_client = new();
        private StreamWriter? m_client_writer;
        private StreamReader m_input_stream;
        private CancellationTokenSource m_forward_token = new();
    }
}
