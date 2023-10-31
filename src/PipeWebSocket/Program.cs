///
/// This program is responsible for piping data from lua code to a websocket connection, through the programs stdin and stdout.
/// Since lua can only read OR write to a program, this project is split into a server configuration and client configuration.
/// Both configurations need to be running and connected to each other for the program to work properly.
/// 
/// The client configuration should be opened as write mode in lua, and server configuration as read mode.
/// Additionally, any messages sent to and received from a PipeWebSocket program should make use of the protocol defined in the Protocol class.
///

using PipeWebSocket;
using System.Net;

switch(args[0])
{
    case "--server":

        if (args.Length != 2)
        {
            Console.WriteLine("Invalid number of command line arguments!");
            return -1;
        }

        Uri websocket_uri = new(args[1]);

        PipeWebSocketServer server = new();
        server.start();
        
        using(Stream stdout = Console.OpenStandardOutput())
        using(StreamWriter stdout_writer = new(stdout))
            await Protocol.sendStream(server.Port.ToString(), stdout_writer);

        await server.acceptPipeWebScoketClient();
        await server.connectWebsocket(websocket_uri);

        await server.pipeData();

        server.stop();
    break;
    case "--client":

        if(args.Length != 3)
        {
            Console.WriteLine("Invalid number of command line arguments!");
            return -1;
        }

        PipeWebSocketClient client = new();
        await client.connect(IPAddress.Parse(args[1]), int.Parse(args[2]));
        await client.pipeData();
    break;
    case "--help":
        Console.WriteLine("Pipe stdin and stdout data to a websocket connection." +
            "\n\n\t--server [WEBSOCKET_URI]" +
            "\n\t\tStart PipeWebSocket in server configuration and connect to a websocket connection at the passed uri." +
            "\n\t\tSends the server port as a message to stdout on startup." +
            "\n\t--client [SERVER_IP] [SERVER_PORT]" +
            "\n\t\tStarts PipeWebSocket in client configuration and connect to a PipeWebSocket program running in server configuration listening at the provided ip and port." +
            "\n\t--help" +
            "\n\t\tWhat you are looking at");    
    break;
    default:
        return -1;
}

return 0;
