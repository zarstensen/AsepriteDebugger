using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

TextWriter pipe_out = Console.Out;

TcpListener writer_server = new(IPAddress.Any, 0);

writer_server.Start(1);

pipe_out.Write(writer_server);
pipe_out.Flush();

TcpClient client = writer_server.AcceptTcpClient();
