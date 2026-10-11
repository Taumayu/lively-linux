namespace Lively.Linux.Daemon;

public interface IPlayer
{
    void SetDisplay(string displayId);
    void Play(string filePath);
    void Pause();
    void Resume();
    void Stop();
    void SendMessage(string msg);
}
