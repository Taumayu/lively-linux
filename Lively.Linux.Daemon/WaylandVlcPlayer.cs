using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Lively.Linux.Daemon
{
    public class WaylandVlcPlayer : IPlayer
    {
        private Process? _process;
        private string? _currentDisplay;

        public void SetDisplay(string displayId)
        {
            _currentDisplay = displayId;
        }

        public void Play(string filePath)
        {
            Stop();

            EnsureKdeRules();

            var vlcPath = "/usr/bin/vlc";
            var livelyVlcPath = Path.Combine(Path.GetTempPath(), "lively-wallpaper-vlc");
            if (!File.Exists(livelyVlcPath))
            {
                try { File.CreateSymbolicLink(livelyVlcPath, vlcPath); }
                catch { livelyVlcPath = "cvlc"; } // fallback if symlink fails
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = livelyVlcPath,
                // Crucial: DO NOT pass --video-wallpaper. It forces fullscreen mode, which causes KWin
                // to disable screen edges and break auto-hiding panels/taskbars.
                Arguments = $"-I dummy --video-title=lively-wallpaper --loop --no-audio --no-osd --no-mouse-events --no-keyboard-events \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine($"Starting VLC on display: {_currentDisplay ?? "Default"} with file: {filePath}");
            _process = Process.Start(startInfo);

            ApplyKdeWindowPropertiesAsync();
        }

        public void Stop()
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                    _process.Dispose();
                }
                catch { }
                _process = null;
            }

            try
            {
                using var pkill = Process.Start(new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = "-c \"pkill -f 'video-title=lively-wallpaper' || true\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                pkill?.WaitForExit(1000);
            }
            catch { }
        }

        public void Pause()
        {
            // Placeholder
        }

        public void Resume()
        {
            // Placeholder
        }

        private static void EnsureKdeRules()
        {
            try
            {
                var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
                if (!desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
                    return;

                string[] commands = [
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key Description \"Lively Wallpaper\"",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key title \"lively-wallpaper\"",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key titlematch 1",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key wmclass \"\"",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key wmclassmatch 0",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key acceptfocus false",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key acceptfocusrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key below true",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key belowrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key fullscreen false",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key fullscreenrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key maximizehoriz true",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key maximizehorizrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key maximizevert true",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key maximizevertrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key noborder true",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key noborderrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key skiptaskbar true",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key skiptaskbarrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key skipswitcher true",
                    "kwriteconfig6 --file kwinrulesrc --group 1 --key skipswitcherrule 2",
                    "kwriteconfig6 --file kwinrulesrc --group General --key count 1",
                    "kwriteconfig6 --file kwinrulesrc --group General --key rules 1",
                    "qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure"
                ];

                var script = string.Join(" && ", commands);
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{script}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                proc?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to configure KDE rules: {ex.Message}");
            }
        }

        private static void ApplyKdeWindowPropertiesAsync()
        {
            Task.Run(async () =>
            {
                var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
                if (!desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase))
                    return;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 500 : 1000);

                    try
                    {
                        var jsScript = @"
var windows = workspace.stackingOrder;
for (var i = 0; i < windows.length; i++) {
    var w = windows[i];
    if (w.caption.indexOf('lively-wallpaper') !== -1) {
        w.onAllDesktops = true;
        w.keepBelow = true;
        w.fullScreen = false;
        w.skipTaskbar = true;
        w.skipSwitcher = true;
        w.noBorder = true;
    }
}
";
                        var tempScript = Path.Combine(Path.GetTempPath(), "lively_kwin_oad.js");
                        await File.WriteAllTextAsync(tempScript, jsScript);

                        var cmd = $"qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.loadScript {tempScript} && " +
                                  "qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.start && " +
                                  $"qdbus6 org.kde.KWin /Scripting org.kde.kwin.Scripting.unloadScript {tempScript}";

                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "bash",
                            Arguments = $"-c \"{cmd}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        proc?.WaitForExit(2000);
                        try { File.Delete(tempScript); } catch { }
                    }
                    catch { }
                }
            });
        }
    }
}

