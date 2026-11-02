using System.Threading;
using System.Threading.Tasks;
using SpotifyVideoImporter.Desktop.Models;

namespace SpotifyVideoImporter.Desktop.Services;

public interface IMetadataService
{
    Task ApplyMetadataAsync(string mp3FilePath, TrackMetadata metadata, CancellationToken ct = default);
    (string Artist, string Title) GuessArtistAndTitle(string rawTitle, string channelName);
    Task<TrackMetadata?> SearchItunesMetadataAsync(string query, CancellationToken ct = default);
    Task<byte[]?> DownloadImageBytesAsync(string imageUrl, CancellationToken ct = default);
}
