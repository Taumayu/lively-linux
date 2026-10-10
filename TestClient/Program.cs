using Grpc.Net.Client;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using System;

class Program
{
    static async Task Main()
    {
        var socketPath = "/tmp/lively-grpc.sock";
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    Console.WriteLine("ConnectCallback invoked!");
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            }
        });
        
        var client = new Lively.Grpc.Common.Proto.Desktop.DesktopServiceClient(channel);
        try 
        {
            await client.SetWallpaperAsync(new Lively.Grpc.Common.Proto.Desktop.SetWallpaperRequest { DisplayId = "1", FilePath = "test" });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
