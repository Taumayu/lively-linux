using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lively.Linux.UI;

public class DisplayInfo
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsEnabled { get; set; }
    
    public override string ToString()
    {
        return string.IsNullOrEmpty(Description) ? Name : $"{Name} ({Description})";
    }
}

public static class WaylandDisplayScanner
{
    public static List<DisplayInfo> GetDisplays()
    {
        var displays = new List<DisplayInfo>();
        try
        {
            var pInfo = new ProcessStartInfo
            {
                FileName = "kscreen-doctor",
                Arguments = "-o",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(pInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split('\n');
                DisplayInfo currentDisplay = null;

                foreach (var line in lines)
                {
                    if (line.StartsWith("Output: "))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length > 2)
                        {
                            currentDisplay = new DisplayInfo { Name = parts[2] };
                            displays.Add(currentDisplay);
                        }
                    }
                    else if (currentDisplay != null && line.Trim().StartsWith("enabled"))
                    {
                        currentDisplay.IsEnabled = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("kscreen-doctor failed: " + ex.Message);
        }

        if (displays.Count > 0)
        {
            return displays.Where(d => d.IsEnabled).ToList();
        }

        try
        {
            var pInfo = new ProcessStartInfo
            {
                FileName = "wlr-randr",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(pInfo);
            if (process == null) return displays;
            
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split('\n');
            DisplayInfo currentDisplay = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (!line.StartsWith(" ") && !line.StartsWith("\t"))
                {
                    var match = Regex.Match(line, @"^(\S+)(?:\s+""(.*)"")?");
                    if (match.Success)
                    {
                        currentDisplay = new DisplayInfo 
                        { 
                            Name = match.Groups[1].Value,
                            Description = match.Groups[2].Success ? match.Groups[2].Value : ""
                        };
                        displays.Add(currentDisplay);
                    }
                }
                else if (currentDisplay != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Enabled:"))
                    {
                        currentDisplay.IsEnabled = trimmed.Contains("yes", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("wlr-randr failed or not found: " + ex.Message);
        }

        if (displays.Count == 0)
        {
            try
            {
                var pInfo = new ProcessStartInfo
                {
                    FileName = "xrandr",
                    Arguments = "--query",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(pInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var lines = output.Split('\n');
                    foreach (var line in lines)
                    {
                        var match = Regex.Match(line, @"^(\S+)\s+connected(?:\s+primary)?\s+(\d+x\d+\+\d+\+\d+)?");
                        if (match.Success)
                        {
                            var name = match.Groups[1].Value;
                            var resolution = match.Groups[2].Success ? match.Groups[2].Value : "";
                            displays.Add(new DisplayInfo
                            {
                                Name = name,
                                Description = resolution,
                                IsEnabled = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("xrandr fallback failed: " + ex.Message);
            }
        }
        
        return displays.Where(d => d.IsEnabled).ToList();
    }
}
