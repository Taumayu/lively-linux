using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Lively.Linux.Daemon
{
    public class WaylandWebPlayer : IPlayer
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

            var scriptPath = ResolveScriptPath();
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                Console.WriteLine($"[WaylandWebPlayer] Could not find lively-web-player.py!");
                return;
            }

            var displayArg = string.IsNullOrEmpty(_currentDisplay) ? "" : $"--monitor \"{_currentDisplay}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" {displayArg} \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true
            };

            Console.WriteLine($"[WaylandWebPlayer] Starting web player on display: {_currentDisplay ?? "Default"} with: {filePath}");
            _process = Process.Start(startInfo);

            ApplyKdeWindowPropertiesAsync();
        }

        public void SendMessage(string msg)
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.StandardInput.WriteLine(msg);
                    _process.StandardInput.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WaylandWebPlayer] Error sending message: {ex.Message}");
                }
            }
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
                    Arguments = "-c \"pkill -f 'lively-web-player.py' || true\"",
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

        private static string? ResolveScriptPath()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "lively-web-player.py"),
                Path.Combine(baseDir, "..", "..", "..", "lively-web-player.py"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lively-linux", "daemon", "lively-web-player.py"),
                "/home/auma/Projects/lively-linux/Lively.Linux.Daemon/lively-web-player.py"
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                    return full;
            }

            return null;
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
                Console.WriteLine($"[WaylandWebPlayer] Failed to configure KDE rules: {ex.Message}");
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
                        var tempScript = Path.Combine(Path.GetTempPath(), "lively_kwin_web.js");
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
