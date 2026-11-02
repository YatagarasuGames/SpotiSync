using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpotifyVideoImporter.Desktop.Models;

namespace SpotifyVideoImporter.Desktop.Services;

public interface IYouTubeService
{
    Task<VideoItem?> GetVideoInfoAsync(string urlOrId, CancellationToken ct = default);
    Task<IReadOnlyList<VideoItem>> SearchVideosAsync(string query, int limit = 15, CancellationToken ct = default);
    Task<string> DownloadAudioOnlyStreamAsync(string videoUrlOrId, IProgress<double>? progress = null, CancellationToken ct = default);
}
