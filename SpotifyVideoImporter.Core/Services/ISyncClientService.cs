using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpotifyVideoImporter.Core.Models;

namespace SpotifyVideoImporter.Core.Services;

public interface ISyncClientService
{
    SyncSessionInfo? ParseQrPayload(string qrData);
    Task<SyncSessionInfo?> DiscoverServerAsync(TimeSpan timeout, CancellationToken ct = default);
    Task<bool> PingServerAsync(SyncSessionInfo session, CancellationToken ct = default);
    Task<IReadOnlyList<SyncTrackDto>> GetTracksAsync(SyncSessionInfo session, CancellationToken ct = default);
    Task DownloadTrackAsync(SyncSessionInfo session, SyncTrackDto track, string targetFolder, IProgress<double>? progress = null, CancellationToken ct = default);
    IReadOnlyList<SyncTrackDto> MarkExistingTracks(IReadOnlyList<SyncTrackDto> serverTracks, string localFolder);
}
