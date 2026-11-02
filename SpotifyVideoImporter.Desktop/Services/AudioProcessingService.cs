using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SpotifyVideoImporter.Desktop.Services;

public class AudioProcessingService : IAudioProcessingService
{
    private static readonly Regex TimeRegex = new(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled);
    private readonly IToolBootstrapService _toolBootstrap;

    public AudioProcessingService(IToolBootstrapService? toolBootstrap = null)
    {
        _toolBootstrap = toolBootstrap ?? new ToolBootstrapService();
    }

    public bool IsFFmpegAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _toolBootstrap.FFmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-version");

            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> TrimAndConvertToMp3Async(
        string inputAudioPath,
        string outputMp3Path,
        TimeSpan start,
        TimeSpan end,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(inputAudioPath))
            throw new FileNotFoundException("Input audio file not found", inputAudioPath);

        await _toolBootstrap.EnsureToolsAsync(null, null, ct);

        var outputDir = Path.GetDirectoryName(outputMp3Path);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var hasStart = start > TimeSpan.Zero;
        var hasEnd = end > start;
        var totalTargetSeconds = hasEnd ? (end - start).TotalSeconds : 0;

        var psi = new ProcessStartInfo
        {
            FileName = _toolBootstrap.FFmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-y");

        if (hasStart)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(string.Format(CultureInfo.InvariantCulture, "{0:hh\\:mm\\:ss\\.fff}", start));
        }

        if (hasEnd)
        {
            psi.ArgumentList.Add("-to");
            psi.ArgumentList.Add(string.Format(CultureInfo.InvariantCulture, "{0:hh\\:mm\\:ss\\.fff}", end));
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputAudioPath);
        psi.ArgumentList.Add("-vn");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("libmp3lame");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("320k");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("44100");
        psi.ArgumentList.Add("-id3v2_version");
        psi.ArgumentList.Add("3");
        psi.ArgumentList.Add(outputMp3Path);

        using var process = new Process { StartInfo = psi };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            if (totalTargetSeconds > 0 && progress != null)
            {
                var match = TimeRegex.Match(e.Data);
                if (match.Success)
                {
                    var hours = int.Parse(match.Groups[1].Value);
                    var minutes = int.Parse(match.Groups[2].Value);
                    var seconds = int.Parse(match.Groups[3].Value);
                    var currentSecs = (hours * 3600) + (minutes * 60) + seconds;
                    var ratio = Math.Clamp(currentSecs / totalTargetSeconds, 0.0, 1.0);
                    progress.Report(ratio);
                }
            }
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg завершился с кодом ошибки {process.ExitCode}");
        }

        progress?.Report(1.0);
        return outputMp3Path;
    }
}