using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpotifyVideoImporter.Desktop.Models;

namespace SpotifyVideoImporter.Desktop.Services;

public class YouTubeService : IYouTubeService
{
    private static readonly Regex ProgressRegex = new(@"\[download\]\s+(\d+(?:\.\d+)?)%", RegexOptions.Compiled);
    private readonly IToolBootstrapService _toolBootstrap;

    public string? ProxyUrl { get; set; }

    public YouTubeService(IToolBootstrapService? toolBootstrap = null)
    {
        _toolBootstrap = toolBootstrap ?? new ToolBootstrapService();
    }

    public async Task<VideoItem?> GetVideoInfoAsync(string urlOrId, CancellationToken ct = default)
    {
        try
        {
            await _toolBootstrap.EnsureToolsAsync(null, null, ct);

            var args = new List<string> { "--dump-single-json", "--no-playlist", "--skip-download" };
            if (!string.IsNullOrWhiteSpace(ProxyUrl))
            {
                args.Add("--proxy");
                args.Add(ProxyUrl.Trim());
            }
            args.Add(urlOrId);

            var json = await ExecuteYtDlpAsync(args, ct);
            if (string.IsNullOrWhiteSpace(json)) return null;

            var doc = JsonSerializer.Deserialize<YtDlpMetadata>(json);
            if (doc == null || string.IsNullOrWhiteSpace(doc.Id)) return null;

            return new VideoItem
            {
                Id = doc.Id,
                Title = doc.Title ?? "Unknown Title",
                Author = doc.Channel ?? doc.Uploader ?? "Unknown Channel",
                Duration = TimeSpan.FromSeconds(doc.Duration ?? 0),
                ThumbnailUrl = doc.Thumbnail ?? string.Empty,
                Url = doc.WebpageUrl ?? (!string.IsNullOrWhiteSpace(doc.Id) ? $"https://www.youtube.com/watch?v={doc.Id}" : urlOrId)
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<VideoItem>> SearchVideosAsync(string query, int limit = 15, CancellationToken ct = default)
    {
        var list = new List<VideoItem>();
        try
        {
            await _toolBootstrap.EnsureToolsAsync(null, null, ct);

            var args = new List<string> { "--dump-json", "--flat-playlist", "--skip-download" };
            if (!string.IsNullOrWhiteSpace(ProxyUrl))
            {
                args.Add("--proxy");
                args.Add(ProxyUrl.Trim());
            }
            args.Add($"ytsearch{limit}:{query}");

            using var process = CreateProcess(args);
            process.Start();

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var item = JsonSerializer.Deserialize<YtDlpMetadata>(line);
                    if (item != null && !string.IsNullOrWhiteSpace(item.Id))
                    {
                        list.Add(new VideoItem
                        {
                            Id = item.Id,
                            Title = item.Title ?? "Unknown Title",
                            Author = item.Channel ?? item.Uploader ?? "Unknown Channel",
                            Duration = TimeSpan.FromSeconds(item.Duration ?? 0),
                            ThumbnailUrl = item.Thumbnail ?? string.Empty,
                            Url = item.WebpageUrl ?? $"https://www.youtube.com/watch?v={item.Id}"
                        });
                    }
                }
                catch
                {
                    // Ignore malformed line
                }
            }

            await process.WaitForExitAsync(ct);
        }
        catch
        {
            // Empty list on error
        }

        return list;
    }

    public async Task<string> DownloadAudioOnlyStreamAsync(string videoUrlOrId, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await _toolBootstrap.EnsureToolsAsync(null, null, ct);

        var tempDir = Path.Combine(Path.GetTempPath(), "SpotiSync");
        Directory.CreateDirectory(tempDir);

        var tempOutputPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.%(ext)s");

        var args = new List<string>
        {
            "-f", "bestaudio/best",
            "-o", tempOutputPath,
            "--no-playlist",
            "--no-warnings"
        };

        if (!string.IsNullOrWhiteSpace(ProxyUrl))
        {
            args.Add("--proxy");
            args.Add(ProxyUrl.Trim());
        }
        args.Add(videoUrlOrId);

        var errorSb = new System.Text.StringBuilder();

        using var process = CreateProcess(args);

        process.OutputDataReceived += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            var match = ProgressRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                progress?.Report(pct / 100.0);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                errorSb.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var err = errorSb.ToString().Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(err)
                ? $"yt-dlp завершился с ошибкой {process.ExitCode}"
                : $"yt-dlp: {err}");
        }

        // Locate the created file (extension can be m4a, opus, webm, etc.)
        var prefix = Path.GetFileNameWithoutExtension(tempOutputPath);
        var foundFiles = Directory.GetFiles(tempDir, $"{prefix}.*");
        if (foundFiles.Length == 0)
        {
            throw new FileNotFoundException("Загруженный аудиофайл не найден на диске.");
        }

        progress?.Report(1.0);
        return foundFiles[0];
    }

    private Process CreateProcess(IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _toolBootstrap.YtDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        return new Process { StartInfo = psi };
    }

    private async Task<string> ExecuteYtDlpAsync(IEnumerable<string> arguments, CancellationToken ct)
    {
        using var process = CreateProcess(arguments);
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return output;
    }

    private class YtDlpMetadata
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("uploader")]
        public string? Uploader { get; set; }

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; set; }

        [JsonPropertyName("webpage_url")]
        public string? WebpageUrl { get; set; }
    }
}