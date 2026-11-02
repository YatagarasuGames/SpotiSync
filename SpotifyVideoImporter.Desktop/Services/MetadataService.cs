using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpotifyVideoImporter.Desktop.Models;

namespace SpotifyVideoImporter.Desktop.Services;

public class MetadataService : IMetadataService
{
    private readonly HttpClient _httpClient;

    // RegEx patterns to clean common YouTube title junk
    private static readonly Regex JunkPattern = new(
        @"\s*([(\[]\s*(official\s*(music\s*)?video|official\s*audio|lyrics?|visualizer|hd|4k|remastered|live|audio|clip|hq)\s*[)\]])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FtPattern = new(
        @"\s*([(\[]\s*(feat|ft)\.?\s+[^)\]]+[)\]])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public MetadataService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SpotifyVideoImporter/1.0");
        }
    }

    public (string Artist, string Title) GuessArtistAndTitle(string rawTitle, string channelName)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return ("Unknown Artist", "Unknown Title");

        // Clean common noise
        var cleaned = JunkPattern.Replace(rawTitle, "");

        // Look for common separators: " - ", " – ", " — ", " : ", " | "
        var separators = new[] { " - ", " – ", " — ", " : ", " | " };
        foreach (var sep in separators)
        {
            var parts = cleaned.Split(new[] { sep }, 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
        }

        // If no separator found, use channel name as artist, cleaned title as title
        var artist = string.IsNullOrWhiteSpace(channelName) ? "Unknown Artist" : channelName.Replace(" - Topic", "").Trim();
        return (artist, cleaned.Trim());
    }

    public async Task ApplyMetadataAsync(string mp3FilePath, TrackMetadata metadata, CancellationToken ct = default)
    {
        if (!File.Exists(mp3FilePath))
            throw new FileNotFoundException("MP3 file not found for tagging", mp3FilePath);

        byte[]? coverData = metadata.CoverData;

        // If cover bytes not already loaded, try downloading from CoverUrl
        if (coverData == null && !string.IsNullOrWhiteSpace(metadata.CoverUrl))
        {
            coverData = await DownloadImageBytesAsync(metadata.CoverUrl, ct);
        }

        // Use TagLibSharp to set ID3v2.3 tags
        using var file = TagLib.File.Create(mp3FilePath);

        // Ensure ID3v2.3 for maximum Spotify compatibility
        file.Tag.Title = metadata.Title;
        file.Tag.Performers = new[] { metadata.Artist };
        file.Tag.AlbumArtists = new[] { metadata.Artist };
        file.Tag.Album = string.IsNullOrWhiteSpace(metadata.Album) ? metadata.Title : metadata.Album;
        file.Tag.Year = metadata.Year;
        file.Tag.Genres = new[] { metadata.Genre };

        if (coverData != null && coverData.Length > 0)
        {
            var picture = new TagLib.Picture(new TagLib.ByteVector(coverData))
            {
                Type = TagLib.PictureType.FrontCover,
                Description = "Cover",
                MimeType = "image/jpeg"
            };

            file.Tag.Pictures = new TagLib.IPicture[] { picture };
        }

        file.Save();
    }

    public async Task<byte[]?> DownloadImageBytesAsync(string imageUrl, CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync(imageUrl, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TrackMetadata?> SearchItunesMetadataAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"https://itunes.apple.com/search?term={encoded}&entity=song&limit=1";
            var response = await _httpClient.GetFromJsonAsync<ItunesSearchResponse>(url, ct);

            var track = response?.Results?.FirstOrDefault();
            if (track == null) return null;

            // Get high-res cover: iTunes artworkUrl100 can be upgraded to 1200x1200bb.jpg
            var highResCover = track.ArtworkUrl100?.Replace("100x100bb.jpg", "1200x1200bb.jpg");

            uint year = (uint)DateTime.Now.Year;
            if (DateTime.TryParse(track.ReleaseDate, out var releaseDate))
            {
                year = (uint)releaseDate.Year;
            }

            return new TrackMetadata
            {
                Title = track.TrackName ?? string.Empty,
                Artist = track.ArtistName ?? string.Empty,
                Album = track.CollectionName ?? string.Empty,
                Year = year,
                Genre = track.PrimaryGenreName ?? "Music",
                CoverUrl = highResCover ?? track.ArtworkUrl100
            };
        }
        catch
        {
            return null;
        }
    }

    private class ItunesSearchResponse
    {
        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("results")]
        public ItunesTrackItem[]? Results { get; set; }
    }

    private class ItunesTrackItem
    {
        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("collectionName")]
        public string? CollectionName { get; set; }

        [JsonPropertyName("releaseDate")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("primaryGenreName")]
        public string? PrimaryGenreName { get; set; }

        [JsonPropertyName("artworkUrl100")]
        public string? ArtworkUrl100 { get; set; }
    }
}
