using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using NAudio.Wave;

namespace SpotifyVideoImporter.Desktop.Services;

[SupportedOSPlatform("windows")]
public class AudioPlayerService : IAudioPlayerService
{
    private WaveOut? _waveOut;
    private WaveStream? _reader;
    private System.Timers.Timer? _progressTimer;

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
    public double PositionSeconds => _reader?.CurrentTime.TotalSeconds ?? 0;
    public double DurationSeconds => _reader?.TotalTime.TotalSeconds ?? 0;

    public event Action<double>? PositionChanged;
    public event Action? PlaybackEnded;

    public AudioPlayerService()
    {
        _progressTimer = new System.Timers.Timer(40); // 25 fps smooth cursor
        _progressTimer.Elapsed += (s, e) =>
        {
            if (IsPlaying && _reader != null)
            {
                PositionChanged?.Invoke(_reader.CurrentTime.TotalSeconds);
            }
        };
    }

    public Task LoadFileAsync(string filePath)
    {
        Stop();
        _reader?.Dispose();
        _waveOut?.Dispose();

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found", filePath);

        try
        {
            _reader = new MediaFoundationReader(filePath);
        }
        catch
        {
            _reader = new AudioFileReader(filePath);
        }

        _waveOut = new WaveOut();
        _waveOut.Init(_reader);
        _waveOut.PlaybackStopped += (s, e) =>
        {
            _progressTimer?.Stop();
            PlaybackEnded?.Invoke();
        };

        return Task.CompletedTask;
    }

    public void Play()
    {
        if (_waveOut != null && _reader != null)
        {
            _waveOut.Play();
            _progressTimer?.Start();
        }
    }

    public void Pause()
    {
        if (_waveOut != null)
        {
            _waveOut.Pause();
            _progressTimer?.Stop();
        }
    }

    public void Stop()
    {
        _progressTimer?.Stop();
        if (_waveOut != null)
        {
            _waveOut.Stop();
        }
        if (_reader != null)
        {
            _reader.CurrentTime = TimeSpan.Zero;
            PositionChanged?.Invoke(0);
        }
    }

    public void Seek(double seconds)
    {
        if (_reader != null)
        {
            var target = Math.Clamp(seconds, 0, _reader.TotalTime.TotalSeconds);
            _reader.CurrentTime = TimeSpan.FromSeconds(target);
            PositionChanged?.Invoke(target);
        }
    }

    public async Task<float[]> GenerateWaveformPeaksAsync(string filePath, int peakCount = 200)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var reader = new AudioFileReader(filePath);
                ISampleProvider sampleProvider = reader;
                var totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
                var samplesPerPeak = (int)Math.Max(1, totalSamples / peakCount);

                var buffer = new float[samplesPerPeak];
                var peaks = new float[peakCount];

                for (int i = 0; i < peakCount; i++)
                {
                    var read = sampleProvider.Read(buffer.AsSpan());
                    if (read == 0) break;

                    float max = 0;
                    for (int n = 0; n < read; n++)
                    {
                        var abs = Math.Abs(buffer[n]);
                        if (abs > max) max = abs;
                    }
                    peaks[i] = max;
                }

                return peaks;
            }
            catch
            {
                return Array.Empty<float>();
            }
        });
    }

    public void Dispose()
    {
        _progressTimer?.Dispose();
        _waveOut?.Dispose();
        _reader?.Dispose();
    }
}
