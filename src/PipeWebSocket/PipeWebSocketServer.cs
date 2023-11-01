using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace PipeWebSocket
{
    /// <summary>
    /// Receives input from PipeWebSocketClient over a tcp connection, and forwards it to a websocket.
    /// Websocket messages are forwarded to an output stream, stdout by default.
    /// </summary>
    public class PipeWebSocketServer
    {
        public int Port => (m_server.LocalEndpoint as IPEndPoint)!.Port;
        public IPAddress Address => (m_server.LocalEndpoint as IPEndPoint)!.Address;
        
        public bool IsRunning => m_server.Server.IsBound;

        /// <summary>
        /// Creates a PipeWebSocketServer instance, which listens for connetions at the supplied address and port,
        /// and forwards output to the passed output_stream.
        /// 
        /// </summary>
        /// <param name="address"> IPAddress.Any by default. </param>
        /// <param name="port"> If not specified, or 0, an avaliable port is automatically selected. </param>
        /// <param name="output_stream"> stdout by default. </param>
        public PipeWebSocketServer(IPAddress? address = null, int port = 0, Stream? output_stream = null)
        {
            m_out_stream_writer = new StreamWriter(output_stream ?? Console.OpenStandardOutput());
            m_server = new(address ?? IPAddress.Any, port);
        }
        
        /// <summary>
        /// Start listening for connections.
        /// </summary>
        public void start() =>
            m_server.Start();

        /// <summary>
        /// Stop listener.
        /// </summary>
        public void stop() =>
            m_server.Stop();

        /// <summary>
        /// Starts a message forwarding task which forwards messages from the PipeWebScoketClient to the websocket,
        /// and from the websocket to the output stream.
        /// 
        /// Task finishes when an exit message is received from the PipeWebSocketClient, or the websocket is forcefully closed.
        /// </summary>
        public async Task pipeData() =>
            await Task.WhenAll(Task.Run(pipeForward), Task.Run(websocketForward));

        /// <summary>
        /// Accept PipeWebSocketClient
        /// </summary>
        public async Task acceptPipeWebScoketClient()
        {
            m_client = await m_server.AcceptTcpClientAsync();
            m_in_stream_reader = new StreamReader(m_client!.GetStream());
        }

        /// <summary>
        /// Connect to the websocket at the passed endpoint.
        /// This websocket will be used for forwarding messages from PipeWebScoketClient and to the output stream.
        /// </summary>
        public async Task connectWebsocket(Uri endpoint) =>
            await m_ws_client.ConnectAsync(endpoint, CancellationToken.None);

        private TcpListener m_server;
        private TcpClient? m_client;
        private ClientWebSocket m_ws_client = new();

        private StreamWriter m_out_stream_writer;
        private StreamReader? m_in_stream_reader;

        private CancellationTokenSource m_pipe_token = new();

        /// <summary>
        /// Async method responsible for forwarding messages from a PipeWebScoketClient to a websocket.
        /// </summary>
        private async Task pipeForward()
        {
            if (m_in_stream_reader == null)
                throw new InvalidOperationException("A TcpClient must be connected to the server.");

            while (true)
            {
                string? msg = await Protocol.receiveStream(m_in_stream_reader, m_pipe_token.Token);

                // exit signal was received
                if (msg == null)
                    break;

                await m_ws_client.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, m_pipe_token.Token);
            }

            await Protocol.exitStream(m_out_stream_writer);
            // make sure websocketForward also exits when an exit signal has been received.
            await m_ws_client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, default);
        }

        /// <summary>
        /// Async method responsible for forwarding websocket messages to output stream.
        /// </summary>
        private async Task websocketForward()
        {
            if(m_ws_client.State != WebSocketState.Open)
                throw new InvalidOperationException("ClientWebSocket must be connected to a websocket endpoint.");

            while (m_ws_client.State != WebSocketState.Closed)
            {
                StringBuilder msg = new();
                WebSocketReceiveResult res;
                byte[] buffer = new byte[256];

                do
                {
                    res = await m_ws_client.ReceiveAsync(buffer, default);
                    msg.Append(Encoding.UTF8.GetString(buffer), 0, res.Count);
                } while (!res.EndOfMessage);

                if (res.MessageType == WebSocketMessageType.Close)
                    break;

                File.AppendAllText("C:\\Users\\Karsten\\Downloads\\LATEST_MESSAGE.txt", msg.ToString());
                await Protocol.sendStream(msg.ToString(), m_out_stream_writer);
            }

            // stop any potentially blocking stream reads from continuing in the pipe forward task,
            // and let it exit.
            m_pipe_token.Cancel();
        }
    }
}
