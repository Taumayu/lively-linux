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
var isWayland = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLower() == "wayland";
var isKde = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToLower().Contains("kde") == true;

Lively.Linux.Daemon.IPlayer videoPlayer = (isWayland && !isKde)
    ? new Lively.Linux.Daemon.WaylandMpvPlayer()
    : new Lively.Linux.Daemon.WaylandVlcPlayer();

Lively.Linux.Daemon.IPlayer webPlayer = new Lively.Linux.Daemon.WaylandWebPlayer();

builder.Services.AddSingleton<Lively.Linux.Daemon.IPlayer>(new Lively.Linux.Daemon.CompositeWallpaperPlayer(videoPlayer, webPlayer));

var app = builder.Build();

app.MapGet("/", () => "Lively Wallpaper Linux Daemon - gRPC Server Running!");

app.MapGrpcService<Lively.Linux.Daemon.DesktopServiceImpl>();

app.Run();
