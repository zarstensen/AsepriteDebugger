using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace PipeWebSocket
{
    /// <summary>
    /// 
    /// Class containing helper functions and constants for receiving from and sending to streams and websockets.
    /// A protocol may send an exit signal to a receiving protocol, where both protocols will invoke the OnExit event,
    /// after the signal has been sent or received.
    /// 
    /// Each protocol message should start with either an exit header of a length header.
    /// 
    /// In case a Protocol class receives an exit message, OnExit is invoked, and null is returned from the receive function.
    /// 
    /// If a message starts with a length header, the next 4 bytes whould store the length of the message (excluding the message header).
    /// Afterwards a message header followed by the message payload should be avaliable in the stream.
    /// 
    /// !<LEN>[length of message: 4 bytes]!<MSG>[message payload: !<LEN> payload bytes]
    /// 
    /// if the message is invalid, null will be returned from receive.
    /// 
    /// </summary>
    public static class Protocol
    {
        public const string EXIT_HEADER = "!<EXIT>";
        public const string LEN_HEADER = "!<LEN>";
        public const string MSG_HEADER = "!<MSG>";


        /// <summary>
        /// Send text message from the passed websocket client
        /// </summary>
        public static async Task sendWebsocket(string msg, ClientWebSocket ws, CancellationToken? token = null) =>
            await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, token ?? CancellationToken.None);

        /// <summary>
        /// Receive text message from the passed websocket.
        /// </summary>
        /// <returns> 
        /// Received string, null if websocket conection was closed.
        /// </returns>
        public static async Task<string?> receiveWebsocket(WebSocket ws, CancellationToken? token = null)
        {
            StringBuilder msg_builder = new();
            WebSocketReceiveResult res;
            byte[] buffer = new byte[256];

            do
            {
                res = await ws.ReceiveAsync(buffer, token ?? CancellationToken.None);

                if (res.MessageType == WebSocketMessageType.Close)
                    return null;

                msg_builder.Append(Encoding.UTF8.GetString(buffer, 0, res.Count));

            } while (!res.EndOfMessage);
            
            return msg_builder.ToString();
        }

        /// <summary>
        /// Sends a protocol encoded string message to the stream, which can be read by Protocol.receive.
        /// </summary>
        public static async Task sendStream(string msg, StreamWriter dest)
        {
            await dest.WriteAsync(LEN_HEADER);
            await dest.WriteAsync(Encoding.UTF8.GetString(BitConverter.GetBytes(msg.Length)));
            await dest.WriteAsync(MSG_HEADER);
            await dest.WriteAsync(msg);
            await dest.FlushAsync();
        }

        /// <summary>
        /// Receives and retreives the message payload from a protocol encoded message.
        /// Returns null if an exit signal was received.
        /// </summary>
        /// <exception cref="FormatException"> Thrown if received data is an invalid format. </exception>
        public static async Task<string?> receiveStream(StreamReader source, CancellationToken token = default)
        {
            string? header = await getStreamHeader(source, token);

            if (header == EXIT_HEADER)
                return null;
            else if (header != LEN_HEADER)
                throw new FormatException($"First header must be '{EXIT_HEADER}' or '{LEN_HEADER}', was '{header}' instead");

            char[] msg_buffer = new char[4];
            int? read_res;

            read_res = await readBlockAsyncCancel(source, msg_buffer, token);

            switch(read_res)
            {
                case null:
                    throw new OperationCanceledException();
                case 0:
                    throw new ChannelClosedException("Source stream was closed mid receive.");
            }

            int msg_len = BitConverter.ToInt32(Array.ConvertAll(msg_buffer, c => (byte)c));

            string? msg_header = await getStreamHeader(source, token);

            if (msg_header != MSG_HEADER)
                throw new FormatException($"Header following '{LEN_HEADER}' must be '{MSG_HEADER}', was '{msg_header}' instead");

            msg_buffer = new char[msg_len];

            read_res = await readBlockAsyncCancel(source, msg_buffer, token);

            switch (read_res)
            {
                case null:
                    throw new OperationCanceledException();
                case 0:
                    throw new ChannelClosedException("Source stream was closed mid receive.");
            }

            return new string(msg_buffer);
        }

        /// <summary>
        /// Send an exit message to the passed stream.
        /// </summary>
        public static async Task exitStream(StreamWriter dest)
        {
            await dest.WriteAsync(EXIT_HEADER);
            await dest.FlushAsync();
        }

        /// <summary>
        /// retreive the header starting at position 0 from the stream.
        /// returns null if no header was found.
        ///
        /// A header is defined as a string surrounded by '!<' and '>'.
        /// 
        /// </summary>
        /// <param name="source"></param>
        private static async Task<string?> getStreamHeader(StreamReader source, CancellationToken token = default)
        {
            StringBuilder header_builder = new();
            char[] buff = new char[1];

            for (int i = 0; i < 2; i++)
            {
                if ((await readBlockAsyncCancel(source, buff, token) ?? 0) == 0)
                    return null;

                header_builder.Append(buff[0]);
            }

            if (header_builder.ToString() != "!<")
                return null;

            do
            {
                if (source.EndOfStream)
                    return null;

                if ((await readBlockAsyncCancel(source, buff, token) ?? 0) == 0)
                    return null;

                header_builder.Append(buff[0]);
            } while (buff[0] != '>');

            return header_builder.ToString();
        }

        /// <summary>
        /// 
        /// Reads data from reader into buffer, using ReadBlockAsync, and guarantees a successfull cancel, no matter the stream source.
        /// 
        /// So this can be used to read from stdin and canceling the read, which is not possible using just ReadBlockAsync and a token.
        /// However, this does just leave the read task running in the background, so the program should exit soon after a cancel,
        /// in order to completely cleanup the console read task.
        /// 
        /// </summary>
        /// <returns> ReadBlockAsync result, null if cancelled. </returns>
        private static async Task<int?> readBlockAsyncCancel(StreamReader reader, Memory<char> buffer, CancellationToken token = default)
        {
            CancellationTokenSource cancel_task_token = new();

            Task<int> read_task = reader.ReadBlockAsync(buffer, token).AsTask();
            
            Task cancel_task = Task.Run(token.WaitHandle.WaitOne, cancel_task_token.Token);

            Task finished_task = await Task.WhenAny(read_task, cancel_task);

            if (finished_task == cancel_task)
                return null;

            cancel_task_token.Cancel();

            return read_task.Result;
        }
    }
}
