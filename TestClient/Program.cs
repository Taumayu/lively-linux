using Grpc.Net.Client;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using System;

class Program
{
    static async Task Main(string[] args)
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
        
        var client = new Lively.Grpc.Common.Proto.Desktop.DesktopService.DesktopServiceClient(channel);
        if (args.Length > 0 && args[0].Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            await client.CloseAllWallpapersAsync(new Lively.Grpc.Common.Proto.Desktop.CloseAllWallpapersRequest());
            Console.WriteLine("CloseAllWallpapers call succeeded!");
            return;
        }

        try 
        {
            await client.SetWallpaperAsync(new Lively.Grpc.Common.Proto.Desktop.SetWallpaperRequest { 
                LivelyInfoPath = args.Length > 0 ? args[0] : "/home/auma/Projects/lively-linux/wallpapers/Fluids/LivelyInfo.json",
                MonitorId = "eDP-1" 
            });
            Console.WriteLine("SetWallpaper call succeeded!");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
