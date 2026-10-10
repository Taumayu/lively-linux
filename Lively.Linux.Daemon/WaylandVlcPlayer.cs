using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Lively.Linux.Daemon
{
    public class WaylandVlcPlayer : IPlayer
    {
        private Process _process;
        private string _currentDisplay;

        public void SetDisplay(string displayId)
        {
            _currentDisplay = displayId;
        }

        public void Play(string filePath)
        {
            Stop();

            var startInfo = new ProcessStartInfo
            {
                FileName = "cvlc", // Command-line VLC
                // --video-wallpaper draws to root window (X11/XWayland).
                // --loop loops the video
                // --no-audio mutes it
                // --no-osd removes on-screen display
                Arguments = $"--video-wallpaper --loop --no-audio --no-osd \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // If we have a specific Wayland output, passing it to VLC natively is tricky, 
            // but we store it for logging/future expansion.
            Console.WriteLine($"Starting VLC on display: {_currentDisplay ?? "Default"} with file: {filePath}");

            _process = Process.Start(startInfo);
        }

        public void Stop()
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.Dispose();
                _process = null;
            }
        }

        public void Pause()
        {
            // Pause isn't easily supported without VLC IPC (e.g., dbus or rc interface)
            // For now, this is a placeholder.
        }

        public void Resume()
        {
            // Placeholder
        }
    }
}
