using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace Lively.Linux.Daemon;

public class WaylandMpvPlayer : IPlayer, IDisposable
{
    private Process? _mpvProcess;
    private string _displayId = "*";

    public void SetDisplay(string displayId)
    {
        _displayId = displayId;
    }

    public void Play(string filePath)
    {
        Stop();

        // On Wayland, "mpvpaper" is a popular utility that wraps mpv and draws to wlr-layer-shell
        // We configure it to expose a unix socket for IPC to allow pausing/resuming from our Daemon.
        var startInfo = new ProcessStartInfo
        {
            FileName = "mpvpaper",
            Arguments = $"-o \"--input-ipc-server=/tmp/mpv-lively.sock --loop-file=inf\" * \"{filePath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _mpvProcess = Process.Start(startInfo);
    }

    public void Pause()
    {
        SendCommand("{\"command\": [\"set_property\", \"pause\", true]}");
    }

    public void Resume()
    {
        SendCommand("{\"command\": [\"set_property\", \"pause\", false]}");
    }

    public void Stop()
    {
        if (_mpvProcess != null && !_mpvProcess.HasExited)
        {
            _mpvProcess.Kill();
            _mpvProcess.Dispose();
        }
        _mpvProcess = null;
    }

    private void SendCommand(string jsonCommand)
    {
        Task.Run(async () => 
        {
            try 
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await client.ConnectAsync(new UnixDomainSocketEndPoint("/tmp/mpv-lively.sock"));
                var bytes = System.Text.Encoding.UTF8.GetBytes(jsonCommand + "\n");
                await client.SendAsync(bytes, SocketFlags.None);
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send IPC command to mpv: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        Stop();
    }
}
