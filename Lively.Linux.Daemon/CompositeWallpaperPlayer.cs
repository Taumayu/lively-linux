using System;
using System.IO;

namespace Lively.Linux.Daemon
{
    public class CompositeWallpaperPlayer : IPlayer
    {
        private readonly IPlayer _videoPlayer;
        private readonly IPlayer _webPlayer;
        private IPlayer? _activePlayer;

        public CompositeWallpaperPlayer(IPlayer videoPlayer, IPlayer webPlayer)
        {
            _videoPlayer = videoPlayer;
            _webPlayer = webPlayer;
        }

        public void SetDisplay(string displayId)
        {
            _videoPlayer.SetDisplay(displayId);
            _webPlayer.SetDisplay(displayId);
        }

        public void Play(string filePath)
        {
            Stop();

            if (IsWebWallpaper(filePath))
            {
                Console.WriteLine($"[CompositeWallpaperPlayer] Routing to WebPlayer: {filePath}");
                _activePlayer = _webPlayer;
            }
            else
            {
                Console.WriteLine($"[CompositeWallpaperPlayer] Routing to VideoPlayer: {filePath}");
                _activePlayer = _videoPlayer;
            }

            _activePlayer.Play(filePath);
        }

        public void Stop()
        {
            _videoPlayer.Stop();
            _webPlayer.Stop();
            _activePlayer = null;
        }

        public void Pause() => _activePlayer?.Pause();

        public void Resume() => _activePlayer?.Resume();

        public void SendMessage(string msg) => _activePlayer?.SendMessage(msg);

        public static bool IsWebWallpaper(string path)
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".html" or ".htm" or ".xhtml" => true,
                ".svg" or ".jpg" or ".jpeg" or ".png" or ".webp" => true,
                _ => false
            };
        }
    }
}
