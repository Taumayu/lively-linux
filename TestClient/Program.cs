using Grpc.Net.Client;
using System.Net.Sockets;
using Lively.Grpc.Common.Proto.Desktop;
using System;
using System.Threading.Tasks;
using System.Net.Http;

var socketPath = "/tmp/lively-grpc.sock";
var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler
    {
        ConnectCallback = async (context, token) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
});

var client = new DesktopService.DesktopServiceClient(channel);

Console.WriteLine("Sending SetWallpaper request to Daemon...");
try {
    await client.SetWallpaperAsync(new SetWallpaperRequest { LivelyInfoPath = "/fake/path/LivelyInfo.json" });
    Console.WriteLine("Success! gRPC IPC works.");
}
catch (Exception e) {
    Console.WriteLine("Error: " + e.Message);
}
