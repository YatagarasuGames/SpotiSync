using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyVideoImporter.Desktop.Services;

public interface IToolBootstrapService
{
    string YtDlpPath { get; }
    string FFmpegPath { get; }
    bool AreToolsAvailable { get; }
    Task<bool> EnsureToolsAsync(IProgress<string>? status = null, IProgress<double>? progress = null, CancellationToken ct = default);
}

public class ToolBootstrapService : IToolBootstrapService
{
    private readonly HttpClient _httpClient;
    private readonly string _toolsDir;
    private string? _resolvedYtDlpPath;
    private string? _resolvedFFmpegPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const string YtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string FFmpegZipUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    public ToolBootstrapService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SpotiSync",
            "tools");
    }

    public string YtDlpPath => _resolvedYtDlpPath ?? "yt-dlp";
    public string FFmpegPath => _resolvedFFmpegPath ?? "ffmpeg";

    public bool AreToolsAvailable =>
        !string.IsNullOrEmpty(_resolvedYtDlpPath) &&
        !string.IsNullOrEmpty(_resolvedFFmpegPath) &&
        File.Exists(_resolvedYtDlpPath) &&
        File.Exists(_resolvedFFmpegPath);

    public async Task<bool> EnsureToolsAsync(
        IProgress<string>? status = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_toolsDir);

            // 1. Resolve or download yt-dlp
            _resolvedYtDlpPath = ResolveExecutable("yt-dlp.exe");
            if (string.IsNullOrEmpty(_resolvedYtDlpPath))
            {
                status?.Report("Загрузка компонента yt-dlp для загрузки аудио...");
                var targetYtDlp = Path.Combine(_toolsDir, "yt-dlp.exe");
                await DownloadFileAsync(YtDlpDownloadUrl, targetYtDlp, progress, ct);
                _resolvedYtDlpPath = targetYtDlp;
            }

            // 2. Resolve or download ffmpeg
            _resolvedFFmpegPath = ResolveExecutable("ffmpeg.exe");
            if (string.IsNullOrEmpty(_resolvedFFmpegPath))
            {
                status?.Report("Загрузка и настройка FFmpeg для конвертации MP3...");
                await DownloadAndExtractFFmpegAsync(status, progress, ct);
                _resolvedFFmpegPath = Path.Combine(_toolsDir, "ffmpeg.exe");
            }

            status?.Report("Все медиа-компоненты (FFmpeg, yt-dlp) готовы к работе.");
            return true;
        }
        catch (Exception ex)
        {
            status?.Report($"Предупреждение: не удалось автоматически скачать инструменты: {ex.Message}");
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string? ResolveExecutable(string exeName)
    {
        // 1. Check local tools directory
        var localPath = Path.Combine(_toolsDir, exeName);
        if (File.Exists(localPath))
        {
            return localPath;
        }

        // 2. Check application directory
        var appPath = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(appPath))
        {
            return appPath;
        }

        var appToolsPath = Path.Combine(AppContext.BaseDirectory, "tools", exeName);
        if (File.Exists(appToolsPath))
        {
            return appToolsPath;
        }

        // 3. Check system PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var fullCandidate = Path.Combine(segment.Trim(), exeName);
                    if (File.Exists(fullCandidate))
                    {
                        return fullCandidate;
                    }
                }
                catch { }
            }
        }

        return null;
    }

    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var tempFile = destinationPath + ".download";
        if (File.Exists(tempFile))
        {
            try { File.Delete(tempFile); } catch { }
        }

        using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
            await using var destStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            var bytesRead = 0;
            var totalRead = 0L;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report((double)totalRead / totalBytes);
                }
            }
        }

        if (File.Exists(destinationPath))
        {
            try { File.Delete(destinationPath); } catch { }
        }

        File.Move(tempFile, destinationPath);
    }

    private async Task DownloadAndExtractFFmpegAsync(
        IProgress<string>? status,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(FFmpegZipUrl, tempZipPath, progress, ct);

            status?.Report("Распаковка FFmpeg...");

            using var archive = ZipFile.OpenRead(tempZipPath);
            foreach (var entry in archive.Entries)
            {
                var entryName = Path.GetFileName(entry.FullName);
                if (entryName.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                    entryName.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var destination = Path.Combine(_toolsDir, entryName);
                    if (File.Exists(destination))
                    {
                        try { File.Delete(destination); } catch { }
                    }
                    entry.ExtractToFile(destination, true);
                }
            }
        }
        finally
        {
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); } catch { }
            }
        }
    }
}