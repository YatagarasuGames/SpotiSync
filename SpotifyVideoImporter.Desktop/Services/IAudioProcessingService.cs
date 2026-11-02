using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyVideoImporter.Desktop.Services;

public interface IAudioProcessingService
{
    Task<string> TrimAndConvertToMp3Async(
        string inputAudioPath,
        string outputMp3Path,
        TimeSpan start,
        TimeSpan end,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    bool IsFFmpegAvailable();
}
