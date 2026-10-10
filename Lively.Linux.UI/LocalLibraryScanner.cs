using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Lively.Models;

namespace Lively.Linux.UI;

public class LocalLibraryScanner
{
    public static List<LivelyInfoModel> ScanLibrary(string libraryPath)
    {
        var wallpapers = new List<LivelyInfoModel>();
        if (!Directory.Exists(libraryPath)) return wallpapers;

        foreach (var dir in Directory.GetDirectories(libraryPath))
        {
            var infoPath = Path.Combine(dir, "LivelyInfo.json");
            if (File.Exists(infoPath))
            {
                try 
                {
                    var json = File.ReadAllText(infoPath);
                    var info = JsonSerializer.Deserialize<LivelyInfoModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (info != null)
                    {
                        info.IsAbsolutePath = true;
                        // Store the path to the LivelyInfo.json file so the UI can send it to the daemon
                        info.Id = infoPath; 
                        
                        if (!string.IsNullOrEmpty(info.Thumbnail) && !Path.IsPathRooted(info.Thumbnail))
                        {
                            info.Thumbnail = "file://" + Path.Combine(dir, info.Thumbnail);
                        }

                        wallpapers.Add(info);
                    }
                }
                catch { }
            }
        }
        return wallpapers;
    }
}
