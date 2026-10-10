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
                FileName = "wlr-randr",
                RedirectStandardOutput = true,
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
        
        return displays.Where(d => d.IsEnabled).ToList();
    }
}
