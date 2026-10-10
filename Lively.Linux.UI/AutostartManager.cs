using System;
using System.IO;

namespace Lively.Linux.UI;

public static class AutostartManager
{
    public static bool IsAutostartEnabled()
    {
        var path = GetAutostartPath();
        return File.Exists(path);
    }

    public static void ToggleAutostart()
    {
        var path = GetAutostartPath();
        
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        else
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lively.Linux.UI");
            
            var desktopFile = $@"
[Desktop Entry]
Type=Application
Name=Lively Wallpaper
Comment=Animated desktop wallpapers
Exec={exePath} --background
StartupNotify=false
Terminal=false
Hidden=false
".Trim();

            File.WriteAllText(path, desktopFile);
        }
    }

    private static string GetAutostartPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
        {
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        return Path.Combine(configHome, "autostart", "lively-linux.desktop");
    }
}
