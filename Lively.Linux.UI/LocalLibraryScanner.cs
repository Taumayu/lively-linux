using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lively.Models;

namespace Lively.Linux.UI;

public class LocalLibraryScanner
{
    public static List<LivelyInfoModel> ScanLibrary(string libraryPath)
    {
        var wallpapers = new List<LivelyInfoModel>();
        var scannedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanDirectory(libraryPath, wallpapers, scannedDirs);

        // If local library is empty or missing bundled wallpapers, check bundled candidates
        var bundledPath = FindBundledWallpapersPath();
        if (!string.IsNullOrEmpty(bundledPath) && Directory.Exists(bundledPath))
        {
            ScanDirectory(bundledPath, wallpapers, scannedDirs);
        }

        return wallpapers;
    }

    private static void ScanDirectory(string path, List<LivelyInfoModel> list, HashSet<string> seenDirs)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        foreach (var dir in Directory.GetDirectories(path))
        {
            var dirName = Path.GetFileName(dir);
            if (seenDirs.Contains(dirName))
                continue;

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
                        info.Id = infoPath; 

                        // Resolve Thumbnail
                        info.Thumbnail = ResolveThumbnailUri(dir, info.Thumbnail);

                        list.Add(info);
                        seenDirs.Add(dirName);
                    }
                }
                catch { }
            }
        }
    }

    private static string ResolveThumbnailUri(string dir, string? thumbFile)
    {
        if (!string.IsNullOrEmpty(thumbFile))
        {
            var candidate = Path.IsPathRooted(thumbFile) ? thumbFile : Path.Combine(dir, thumbFile);
            if (File.Exists(candidate))
                return "file://" + candidate;
        }

        // Fallbacks
        string[] fallbacks = ["thumbnail.jpg", "thumbnail.png", "lively_t.jpg", "thumb.jpg", "5hhxt0u4.jpg", "preview.gif", "preview2.gif"];
        foreach (var fb in fallbacks)
        {
            var candidate = Path.Combine(dir, fb);
            if (File.Exists(candidate))
                return "file://" + candidate;
        }

        return string.Empty;
    }

    public static void EnsureDefaultWallpapers(string targetDir)
    {
        try
        {
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            var bundledPath = FindBundledWallpapersPath();
            if (string.IsNullOrEmpty(bundledPath) || !Directory.Exists(bundledPath))
                return;

            foreach (var dir in Directory.GetDirectories(bundledPath))
            {
                var folderName = Path.GetFileName(dir);
                var targetSubDir = Path.Combine(targetDir, folderName);

                if (!Directory.Exists(targetSubDir) && File.Exists(Path.Combine(dir, "LivelyInfo.json")))
                {
                    CopyDirectory(dir, targetSubDir);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("EnsureDefaultWallpapers failed: " + ex.Message);
        }
    }

    public static string? FindBundledWallpapersPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "wallpapers"),
            Path.Combine(baseDir, "..", "..", "..", "..", "wallpapers"),
            Path.Combine(baseDir, "..", "..", "..", "wallpapers"),
            Path.Combine(Environment.CurrentDirectory, "wallpapers"),
            "/usr/share/lively-linux/wallpapers",
            "/home/auma/Projects/lively-linux/wallpapers"
        };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full))
                return full;
        }
        return null;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
