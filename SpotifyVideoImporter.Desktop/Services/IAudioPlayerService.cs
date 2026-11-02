using System;
using System.Threading.Tasks;

namespace SpotifyVideoImporter.Desktop.Services;

public interface IAudioPlayerService : IDisposable
{
    bool IsPlaying { get; }
    double PositionSeconds { get; }
    double DurationSeconds { get; }

    event Action<double>? PositionChanged;
    event Action? PlaybackEnded;

    Task LoadFileAsync(string filePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(double seconds);
    Task<float[]> GenerateWaveformPeaksAsync(string filePath, int peakCount = 200);
}
