using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// For Linux, we use a Unix Domain Socket which maps nicely to Named Pipes conceptually for local IPC.
var socketPath = Path.Combine(Path.GetTempPath(), "lively-grpc.sock");
if (File.Exists(socketPath))
{
    File.Delete(socketPath);
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenUnixSocket(socketPath, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc();
builder.Services.AddSingleton<Lively.Linux.Daemon.IPlayer, Lively.Linux.Daemon.WaylandVlcPlayer>();

var app = builder.Build();

app.MapGet("/", () => "Lively Wallpaper Linux Daemon - gRPC Server Running!");

app.MapGrpcService<Lively.Linux.Daemon.DesktopServiceImpl>();

app.Run();
