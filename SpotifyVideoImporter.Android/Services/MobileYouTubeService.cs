using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyVideoImporter.Android.Services;

public class YouTubeVideoResult
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string DurationFormatted { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Url => $"https://www.youtube.com/watch?v={VideoId}";
}

public class ITunesMetadataResult
{
    public string TrackName { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string AlbumName { get; set; } = string.Empty;
    public string ArtworkUrl { get; set; } = string.Empty;
    public byte[]? ArtworkBytes { get; set; }
}

public class MobileYouTubeService
{
    private readonly HttpClient _http;

    private static readonly string[] CobaltInstances =
    {
        "https://api.cobalt.tools",
        "https://cobalt-api.kavin.rocks",
        "https://api.cobalt7.com"
    };

    private static readonly string[] PipedInstances =
    {
        "https://pipedapi.kavin.rocks",
        "https://api.piped.private.coffee",
        "https://piped-api.lunar.icu"
    };

    public MobileYouTubeService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36");
    }

    /// <summary>
    /// Searches YouTube for tracks using public search APIs.
    /// </summary>
    public async Task<List<YouTubeVideoResult>> SearchVideosAsync(string query, CancellationToken ct = default)
    {
        var results = new List<YouTubeVideoResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        foreach (var instance in PipedInstances)
        {
            try
            {
                var searchUrl = $"{instance}/search?q={Uri.EscapeDataString(query)}&filter=music_songs";
                using var response = await _http.GetAsync(searchUrl, ct);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var url = item.TryGetProperty("url", out var u) ? u.GetString() : string.Empty;
                        var title = item.TryGetProperty("title", out var t) ? t.GetString() : string.Empty;
                        var uploader = item.TryGetProperty("uploaderName", out var up) ? up.GetString() : string.Empty;
                        var duration = item.TryGetProperty("duration", out var d) ? d.GetInt64() : 0;
                        var thumb = item.TryGetProperty("thumbnail", out var th) ? th.GetString() : string.Empty;

                        var videoId = url?.Replace("/watch?v=", "") ?? string.Empty;
                        if (!string.IsNullOrEmpty(videoId) && !string.IsNullOrEmpty(title))
                        {
                            results.Add(new YouTubeVideoResult
                            {
                                VideoId = videoId,
                                Title = title,
                                Author = uploader ?? "Unknown",
                                DurationFormatted = TimeSpan.FromSeconds(duration).ToString(@"mm\:ss"),
                                ThumbnailUrl = thumb ?? string.Empty
                            });
                        }
                    }
                }

                if (results.Count > 0) return results;
            }
            catch
            {
                // Try next instance
            }
        }

        // Fallback: If query is already a direct YouTube URL or video ID
        var directId = ExtractVideoId(query);
        if (!string.IsNullOrEmpty(directId))
        {
            results.Add(new YouTubeVideoResult
            {
                VideoId = directId,
                Title = "YouTube Video",
                Author = "YouTube",
                DurationFormatted = "00:00",
                ThumbnailUrl = $"https://img.youtube.com/vi/{directId}/hqdefault.jpg"
            });
        }

        return results;
    }

    /// <summary>
    /// Downloads YouTube audio to a local cache file (MP3 format).
    /// </summary>
    public async Task<string> DownloadAudioAsync(string youtubeUrlOrId, string cacheDirectory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            Directory.CreateDirectory(cacheDirectory);
        }

        var videoId = ExtractVideoId(youtubeUrlOrId) ?? youtubeUrlOrId;
        var fullYoutubeUrl = $"https://www.youtube.com/watch?v={videoId}";
        var tempFile = Path.Combine(cacheDirectory, $"{videoId}_{Guid.NewGuid():N}.mp3");

        // 1. Try Cobalt API instances
        foreach (var cobalt in CobaltInstances)
        {
            try
            {
                var payload = new
                {
                    url = fullYoutubeUrl,
                    downloadMode = "audio",
                    audioFormat = "mp3"
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, $"{cobalt}/api/json")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                req.Headers.Add("Accept", "application/json");

                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                string? downloadStreamUrl = null;
                if (doc.RootElement.TryGetProperty("url", out var u))
                {
                    downloadStreamUrl = u.GetString();
                }

                if (!string.IsNullOrEmpty(downloadStreamUrl))
                {
                    await DownloadFileAsync(downloadStreamUrl, tempFile, progress, ct);
                    if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 10000)
                    {
                        return tempFile;
                    }
                }
            }
            catch
            {
                // Try next
            }
        }

        // 2. Try Piped audio streams
        foreach (var piped in PipedInstances)
        {
            try
            {
                var streamInfoUrl = $"{piped}/streams/{videoId}";
                using var resp = await _http.GetAsync(streamInfoUrl, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("audioStreams", out var audioStreams))
                {
                    string? bestAudioUrl = null;
                    foreach (var s in audioStreams.EnumerateArray())
                    {
                        if (s.TryGetProperty("url", out var urlProp))
                        {
                            bestAudioUrl = urlProp.GetString();
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(bestAudioUrl))
                    {
                        await DownloadFileAsync(bestAudioUrl, tempFile, progress, ct);
                        if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 10000)
                        {
                            return tempFile;
                        }
                    }
                }
            }
            catch
            {
                // Try next
            }
        }

        throw new InvalidOperationException("Не удалось получить аудиопоток с YouTube. Проверьте подключение к сети.");
    }

    private async Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destinationPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await file.WriteAsync(buffer, 0, read, ct);
            totalRead += read;
            if (totalBytes > 0 && progress != null)
            {
                progress.Report((double)totalRead / totalBytes);
            }
        }
    }

    /// <summary>
    /// Searches iTunes Search API for song metadata and HD artwork.
    /// </summary>
    public async Task<ITunesMetadataResult?> SearchITunesAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        try
        {
            var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&entity=song&limit=1";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var first = results[0];
                var trackName = first.TryGetProperty("trackName", out var t) ? t.GetString() : string.Empty;
                var artistName = first.TryGetProperty("artistName", out var a) ? a.GetString() : string.Empty;
                var albumName = first.TryGetProperty("collectionName", out var c) ? c.GetString() : string.Empty;
                var art = first.TryGetProperty("artworkUrl100", out var artProp) ? artProp.GetString() : string.Empty;

                byte[]? artBytes = null;
                if (!string.IsNullOrEmpty(art))
                {
                    // 1200x1200bb.jpg high-resolution cover
                    var hdArt = art.Replace("100x100bb.jpg", "1200x1200bb.jpg");
                    try
                    {
                        artBytes = await _http.GetByteArrayAsync(hdArt, ct);
                    }
                    catch
                    {
                        try { artBytes = await _http.GetByteArrayAsync(art, ct); } catch { }
                    }
                }

                return new ITunesMetadataResult
                {
                    TrackName = trackName ?? string.Empty,
                    ArtistName = artistName ?? string.Empty,
                    AlbumName = albumName ?? string.Empty,
                    ArtworkUrl = art ?? string.Empty,
                    ArtworkBytes = artBytes
                };
            }
        }
        catch { }

        return null;
    }

    public static string? ExtractVideoId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (input.Length == 11 && !input.Contains('/') && !input.Contains('?'))
        {
            return input;
        }

        if (input.Contains("youtu.be/"))
        {
            var part = input.Split("youtu.be/")[1];
            var end = part.IndexOfAny(new[] { '?', '&', '/' });
            return end > 0 ? part[..end] : part;
        }

        if (input.Contains("watch?v="))
        {
            var part = input.Split("watch?v=")[1];
            var end = part.IndexOfAny(new[] { '&', '#' });
            return end > 0 ? part[..end] : part;
        }

        if (input.Contains("/shorts/"))
        {
            var part = input.Split("/shorts/")[1];
            var end = part.IndexOfAny(new[] { '?', '&', '/' });
            return end > 0 ? part[..end] : part;
        }

        return null;
    }
}
