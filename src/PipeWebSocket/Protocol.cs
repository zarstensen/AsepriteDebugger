using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WSPipeServer
{
    /// <summary>
    /// 
    /// Class containing helper functions and constants for receiving from and sending to streams.
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

        public static Action? OnExit;

        /// <summary>
        /// 
        /// Sends a protocol encoded string message to the stream, which can be read by Protocol.receive.
        /// 
        /// </summary>
        public static void send(string msg, StreamWriter dest)
        {
            dest.Write(LEN_HEADER);
            dest.Write(Encoding.UTF8.GetString(BitConverter.GetBytes(msg.Length)));
            dest.Write(MSG_HEADER);
            dest.Write(msg);
            dest.Flush();
        }

        /// <summary>
        /// 
        /// Receives and retreives the message payload from a protocol encoded message.
        /// Returns null if the message is invalid, or an exit signal was received.
        /// Also invokes OnExit if an exit signal was received.
        /// 
        /// </summary>
        public static string? receive(StreamReader source)
        {
            string? header = getHeader(source);

            if (header == EXIT_HEADER)
            {
                OnExit?.Invoke();
                return null;
            }
            else if (header != LEN_HEADER)
                return null;

            char[] msg_buffer = new char[4];

            source.Read(msg_buffer, 0, msg_buffer.Length);

            int msg_len = BitConverter.ToInt32(Array.ConvertAll(msg_buffer, c => (byte)c));

            if (getHeader(source) != MSG_HEADER)
                return null;

            msg_buffer = new char[msg_len];

            source.Read(msg_buffer, 0, msg_buffer.Length);

            return new string(msg_buffer);
        }

        /// <summary>
        /// 
        /// Send an exit message to the passed stream.
        /// Invokes OnExit on the current Protocol, and on a receiving protocol.
        /// 
        /// </summary>
        /// <param name="dest"></param>
        public static void exit(StreamWriter dest)
        {
            dest.Write(EXIT_HEADER);
            dest.Flush();
            OnExit?.Invoke();
        }

        /// <summary>
        /// retreive the header starting at position 0 from the stream.
        /// returns null if no header was found.
        ///
        /// A header is defined as a string surrounded by '!<' and '>'.
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private static string? getHeader(StreamReader source)
        {
            StringBuilder header_builder = new();

            for (int i = 0; i < 2; i++)
                header_builder.Append((char)source.Read());

            if (header_builder.ToString() != "!<")
                return null;

            char c;

            do
            {
                if (source.EndOfStream)
                    return null;

                c = (char)source.Read();
                header_builder.Append(c);
            } while (c != '>');

            return header_builder.ToString();
        }
    }
}
