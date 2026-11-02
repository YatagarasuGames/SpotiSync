using System;
using System.IO;
using System.Threading;
using Android.Media;

namespace SpotifyVideoImporter.Android.Services;

public interface IAndroidAudioPlayerService : IDisposable
{
    event Action<double>? PositionChanged;
    event Action? PlaybackEnded;

    bool IsPlaying { get; }
    double DurationSeconds { get; }
    double CurrentPositionSeconds { get; }

    void Load(string filePath);
    void Play();
    void Pause();
    void Seek(double seconds);
    void Stop();
}

public class AndroidAudioPlayerService : IAndroidAudioPlayerService
{
    private MediaPlayer? _player;
    private Timer? _timer;
    private string? _currentFilePath;

    public event Action<double>? PositionChanged;
    public event Action? PlaybackEnded;

    public bool IsPlaying => _player?.IsPlaying ?? false;

    public double DurationSeconds
    {
        get
        {
            if (_player == null) return 0;
            try { return _player.Duration / 1000.0; }
            catch { return 0; }
        }
    }

    public double CurrentPositionSeconds
    {
        get
        {
            if (_player == null) return 0;
            try { return _player.CurrentPosition / 1000.0; }
            catch { return 0; }
        }
    }

    public void Load(string filePath)
    {
        Stop();
        _currentFilePath = filePath;

        if (!File.Exists(filePath)) return;

        try
        {
            _player = new MediaPlayer();
            _player.SetDataSource(filePath);
            _player.Prepare();

            _player.Completion += (s, e) =>
            {
                PlaybackEnded?.Invoke();
            };

            _timer = new Timer(_ =>
            {
                if (IsPlaying)
                {
                    PositionChanged?.Invoke(CurrentPositionSeconds);
                }
            }, null, 0, 100);
        }
        catch
        {
            _player?.Dispose();
            _player = null;
        }
    }

    public void Play()
    {
        if (_player != null && !_player.IsPlaying)
        {
            _player.Start();
        }
    }

    public void Pause()
    {
        if (_player != null && _player.IsPlaying)
        {
            _player.Pause();
        }
    }

    public void Seek(double seconds)
    {
        if (_player != null)
        {
            try
            {
                int ms = (int)(seconds * 1000);
                _player.SeekTo(ms);
            }
            catch { }
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        if (_player != null)
        {
            try
            {
                if (_player.IsPlaying) _player.Stop();
                _player.Release();
                _player.Dispose();
            }
            catch { }
            _player = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
