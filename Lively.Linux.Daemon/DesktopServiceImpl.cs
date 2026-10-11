using System.Threading.Tasks;
using Grpc.Core;
using Lively.Grpc.Common.Proto.Desktop;
using Google.Protobuf.WellKnownTypes;
using Lively.Models;
using Newtonsoft.Json;
using System.IO;

namespace Lively.Linux.Daemon;

public class DesktopServiceImpl : DesktopService.DesktopServiceBase
{
    private readonly IPlayer _player;

    public DesktopServiceImpl(IPlayer player)
    {
        _player = player;
    }

    public override Task<Empty> SetWallpaper(SetWallpaperRequest request, ServerCallContext context)
    {
        // 1. request.LivelyInfoPath usually points to LivelyInfo.json.
        // We parse it to find the actual media file to play.
        string actualFilePath = request.LivelyInfoPath;
        
        if (File.Exists(request.LivelyInfoPath) && request.LivelyInfoPath.EndsWith("LivelyInfo.json"))
        {
            var json = File.ReadAllText(request.LivelyInfoPath);
            var info = JsonConvert.DeserializeObject<LivelyInfoModel>(json);
            
            if (info != null && !string.IsNullOrEmpty(info.FileName))
            {
                var dir = Path.GetDirectoryName(request.LivelyInfoPath);
                actualFilePath = Path.Combine(dir ?? string.Empty, info.FileName);
            }
        }
        
        // 2. Play the wallpaper using our Wayland player
        if (!string.IsNullOrEmpty(request.MonitorId))
        {
            _player.SetDisplay(request.MonitorId);
        }
        _player.Play(actualFilePath);
        
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> CloseAllWallpapers(CloseAllWallpapersRequest request, ServerCallContext context)
    {
        _player.Stop();
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> SendMessageWallpaper(WallpaperMessageRequest request, ServerCallContext context)
    {
        if (!string.IsNullOrEmpty(request.Msg))
        {
            _player.SendMessage(request.Msg);
        }
        return Task.FromResult(new Empty());
    }
}
