using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpotifyVideoImporter.Desktop.Models;
using SpotifyVideoImporter.Desktop.Services;

namespace SpotifyVideoImporter.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IYouTubeService _youTubeService;
    private readonly IAudioProcessingService _audioProcessingService;
    private readonly IMetadataService _metadataService;
    private readonly ISpotifyFolderService _spotifyFolderService;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly ISyncServerService _syncServerService;
    private readonly IToolBootstrapService _toolBootstrap;
    private readonly HttpClient _httpClient;

    private string? _cachedPreviewAudioPath;

    public MainViewModel()
        : this(
            new ToolBootstrapService(),
            new SpotifyFolderService(),
            new MetadataService(),
            new AudioPlayerService(),
            new SyncServerService())
    {
    }

    public MainViewModel(
        IToolBootstrapService toolBootstrap,
        ISpotifyFolderService spotifyFolderService,
        IMetadataService metadataService,
        IAudioPlayerService audioPlayerService,
        ISyncServerService syncServerService)
    {
        _toolBootstrap = toolBootstrap;
        _spotifyFolderService = spotifyFolderService;
        _metadataService = metadataService;
        _audioPlayerService = audioPlayerService;
        _syncServerService = syncServerService;
        _youTubeService = new YouTubeService(toolBootstrap);
        _audioProcessingService = new AudioProcessingService(toolBootstrap);
        _httpClient = new HttpClient();

        TargetFolder = _spotifyFolderService.GetDefaultSpotifyFolder();
        IsFFmpegReady = _audioProcessingService.IsFFmpegAvailable();

        SearchResults = new ObservableCollection<VideoItem>();

        _audioPlayerService.PositionChanged += OnAudioPositionChanged;
        _audioPlayerService.PlaybackEnded += OnAudioPlaybackEnded;

        // Automatically start local sync server
        _ = StartSyncServerAsync();

        // Automatically check and bootstrap FFmpeg & yt-dlp if missing
        Task.Run(async () =>
        {
            var statusProg = new Progress<string>(s =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusMessage = s);
            });
            var pctProg = new Progress<double>(p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"Загрузка медиа-компонентов: {p:P0}";
                });
            });

            var ok = await _toolBootstrap.EnsureToolsAsync(statusProg, pctProg);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsFFmpegReady = _audioProcessingService.IsFFmpegAvailable();
            });
        });
    }

    #region Tab Selection
    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = 0;
    #endregion

    #region Search State
    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    public ObservableCollection<VideoItem> SearchResults { get; }
    #endregion

    #region Browser State
    [ObservableProperty]
    public partial string BrowserUrl { get; set; } = "https://www.youtube.com";

    [ObservableProperty]
    public partial string BrowserStatusText { get; set; } = "Готов к работе";
    #endregion

    #region Active Video / Editor State
    [ObservableProperty]
    public partial VideoItem? SelectedVideo { get; set; }

    [ObservableProperty]
    public partial bool HasSelectedVideo { get; set; }

    [ObservableProperty]
    public partial Bitmap? CoverImage { get; set; }

    [ObservableProperty]
    public partial string CoverImageUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial byte[]? CustomCoverBytes { get; set; }

    public Func<Task<byte[]?>>? PickCoverImageHandler { get; set; }

    [ObservableProperty]
    public partial string TrackTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrackArtist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrackAlbum { get; set; } = string.Empty;

    [ObservableProperty]
    public partial uint TrackYear { get; set; } = (uint)DateTime.Now.Year;

    [ObservableProperty]
    public partial string TrackGenre { get; set; } = "Music";

    [ObservableProperty]
    public partial double VideoDurationSeconds { get; set; }

    [ObservableProperty]
    public partial double TrimStartSeconds { get; set; }

    partial void OnTrimStartSecondsChanged(double value)
    {
        if (value > TrimEndSeconds)
            TrimEndSeconds = value;
        UpdateTrimTimeTexts();
    }

    [ObservableProperty]
    public partial double TrimEndSeconds { get; set; }

    partial void OnTrimEndSecondsChanged(double value)
    {
        if (value < TrimStartSeconds)
            TrimStartSeconds = value;
        UpdateTrimTimeTexts();
    }

    [ObservableProperty]
    public partial string TrimStartTimeText { get; set; } = "00:00";

    [ObservableProperty]
    public partial string TrimEndTimeText { get; set; } = "00:00";

    [ObservableProperty]
    public partial string SelectedDurationText { get; set; } = "00:00";

    [ObservableProperty]
    public partial string TargetFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProxyUrl { get; set; } = string.Empty;

    partial void OnProxyUrlChanged(string value)
    {
        if (_youTubeService is YouTubeService yt)
        {
            yt.ProxyUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    [ObservableProperty]
    public partial bool IsFFmpegReady { get; set; }
    #endregion

    #region Vegas Pro Timeline & Audio Preview
    [ObservableProperty]
    public partial double CurrentPlayheadSeconds { get; set; }

    [ObservableProperty]
    public partial string CurrentPlayheadTimeText { get; set; } = "00:00.00";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingPreview { get; set; }

    [ObservableProperty]
    public partial float[]? WaveformPeaks { get; set; }
    #endregion

    #region Import & Progress State
    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial double ImportProgress { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Готов к импорту";

    [ObservableProperty]
    public partial string? LastSavedFilePath { get; set; }
    #endregion

    #region QR Code Sync Server
    [ObservableProperty]
    public partial bool IsSyncServerRunning { get; set; }

    [ObservableProperty]
    public partial string SyncServerAddressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncToken { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncQrPayload { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Bitmap? QrCodeImage { get; set; }

    [ObservableProperty]
    public partial int TotalLocalTracksCount { get; set; }

    [RelayCommand]
    public async Task StartSyncServerAsync()
    {
        await _syncServerService.StartAsync(TargetFolder);
        IsSyncServerRunning = _syncServerService.IsRunning;
        SyncServerAddressText = $"http://{_syncServerService.LocalIpAddress}:{_syncServerService.Port}";
        SyncToken = _syncServerService.CurrentToken ?? string.Empty;
        SyncQrPayload = _syncServerService.QrPayload ?? string.Empty;

        if (_syncServerService.QrCodePngBytes != null)
        {
            using var ms = new MemoryStream(_syncServerService.QrCodePngBytes);
            QrCodeImage = new Bitmap(ms);
        }

        UpdateLocalTracksCount();
    }

    [RelayCommand]
    public async Task CopyQrLinkAsync()
    {
        if (!string.IsNullOrEmpty(SyncQrPayload))
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(SyncQrPayload);
                StatusMessage = "Ссылка для синхронизации скопирована в буфер обмена!";
            }
        }
    }

    [RelayCommand]
    public async Task RestartSyncServerAsync()
    {
        await StartSyncServerAsync();
    }

    private void UpdateLocalTracksCount()
    {
        if (Directory.Exists(TargetFolder))
        {
            TotalLocalTracksCount = Directory.GetFiles(TargetFolder, "*.mp3").Length;
        }
        else
        {
            TotalLocalTracksCount = 0;
        }
    }
    #endregion

    #region Commands
    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsSearching = true;
        StatusMessage = "Поиск видео...";

        try
        {
            var query = SearchQuery.Trim();

            // Check if user entered a direct YouTube URL
            if (query.Contains("youtube.com") || query.Contains("youtu.be"))
            {
                var video = await _youTubeService.GetVideoInfoAsync(query);
                if (video != null)
                {
                    SearchResults.Clear();
                    SearchResults.Add(video);
                    await SelectVideoAsync(video);
                    return;
                }
            }

            // Keyword search
            var results = await _youTubeService.SearchVideosAsync(query, 12);
            SearchResults.Clear();
            foreach (var r in results)
            {
                SearchResults.Add(r);
            }

            StatusMessage = $"Найдено видео: {SearchResults.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task SelectVideoAsync(VideoItem video)
    {
        // Stop any ongoing preview
        _audioPlayerService.Stop();
        IsPlaying = false;
        _cachedPreviewAudioPath = null;
        WaveformPeaks = null;

        SelectedVideo = video;
        HasSelectedVideo = true;

        // Auto-guess artist and title from YouTube title and channel
        var (artist, title) = _metadataService.GuessArtistAndTitle(video.Title, video.Author);
        TrackTitle = title;
        TrackArtist = artist;
        TrackAlbum = title;
        TrackYear = (uint)DateTime.Now.Year;
        TrackGenre = "Music";
        CoverImageUrl = video.ThumbnailUrl;

        // Trimming setup
        VideoDurationSeconds = video.Duration.TotalSeconds;
        TrimStartSeconds = 0;
        TrimEndSeconds = VideoDurationSeconds;
        CurrentPlayheadSeconds = 0;
        UpdateTrimTimeTexts();
        UpdatePlayheadText(0);

        // Load thumbnail as cover image
        await LoadCoverImageAsync(video.ThumbnailUrl);

        StatusMessage = $"Выбрано: {video.Title}";

        // Preload preview audio in background for instant waveform and playback
        _ = LoadPreviewAudioAsync();
    }

    private async Task LoadPreviewAudioAsync()
    {
        if (SelectedVideo == null) return;
        IsLoadingPreview = true;
        StatusMessage = "Загрузка аудиодорожки для таймлайна...";

        try
        {
            var audioFile = await _youTubeService.DownloadAudioOnlyStreamAsync(SelectedVideo.Url);
            _cachedPreviewAudioPath = audioFile;

            await _audioPlayerService.LoadFileAsync(audioFile);
            WaveformPeaks = await _audioPlayerService.GenerateWaveformPeaksAsync(audioFile, 250);

            StatusMessage = "Аудиодорожка готова к предпросмотру.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Предпросмотр недоступен: {ex.Message}";
        }
        finally
        {
            IsLoadingPreview = false;
        }
    }

    [RelayCommand]
    public async Task TogglePlayPreviewAsync()
    {
        if (IsPlaying)
        {
            _audioPlayerService.Pause();
            IsPlaying = false;
        }
        else
        {
            if (_cachedPreviewAudioPath == null && SelectedVideo != null)
            {
                await LoadPreviewAudioAsync();
            }

            _audioPlayerService.Play();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    public void StopPreview()
    {
        _audioPlayerService.Stop();
        IsPlaying = false;
        CurrentPlayheadSeconds = TrimStartSeconds;
        _audioPlayerService.Seek(TrimStartSeconds);
        UpdatePlayheadText(TrimStartSeconds);
    }

    [RelayCommand]
    public void JumpToStart()
    {
        CurrentPlayheadSeconds = TrimStartSeconds;
        _audioPlayerService.Seek(TrimStartSeconds);
        UpdatePlayheadText(TrimStartSeconds);
    }

    [RelayCommand]
    public void JumpToEnd()
    {
        CurrentPlayheadSeconds = TrimEndSeconds;
        _audioPlayerService.Seek(TrimEndSeconds);
        UpdatePlayheadText(TrimEndSeconds);
    }

    [RelayCommand]
    public void SetInPointToPlayhead()
    {
        TrimStartSeconds = CurrentPlayheadSeconds;
        UpdateTrimTimeTexts();
    }

    [RelayCommand]
    public void SetOutPointToPlayhead()
    {
        TrimEndSeconds = CurrentPlayheadSeconds;
        UpdateTrimTimeTexts();
    }

    public void SeekTo(double seconds)
    {
        CurrentPlayheadSeconds = seconds;
        _audioPlayerService.Seek(seconds);
        UpdatePlayheadText(seconds);
    }

    public void SetTrimRange(double start, double end)
    {
        TrimStartSeconds = start;
        TrimEndSeconds = end;
        UpdateTrimTimeTexts();
    }

    [RelayCommand]
    public void ResetTrim()
    {
        TrimStartSeconds = 0;
        TrimEndSeconds = VideoDurationSeconds;
        UpdateTrimTimeTexts();
    }

    [RelayCommand]
    public async Task AutoFillMetadataAsync()
    {
        if (string.IsNullOrWhiteSpace(TrackTitle)) return;

        StatusMessage = "Поиск метаданных в iTunes...";
        var query = $"{TrackArtist} {TrackTitle}".Trim();

        try
        {
            var itunesInfo = await _metadataService.SearchItunesMetadataAsync(query);
            if (itunesInfo != null)
            {
                TrackTitle = itunesInfo.Title;
                TrackArtist = itunesInfo.Artist;
                TrackAlbum = itunesInfo.Album;
                TrackYear = itunesInfo.Year;
                TrackGenre = itunesInfo.Genre;

                if (!string.IsNullOrWhiteSpace(itunesInfo.CoverUrl))
                {
                    CustomCoverBytes = null;
                    CoverImageUrl = itunesInfo.CoverUrl;
                    await LoadCoverImageAsync(itunesInfo.CoverUrl);
                }

                StatusMessage = "Метаданные успешно заполнены из iTunes!";
            }
            else
            {
                StatusMessage = "Трек не найден в iTunes, оставлены текущие теги.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось получить метаданные: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task PickCustomCoverAsync()
    {
        if (PickCoverImageHandler != null)
        {
            var bytes = await PickCoverImageHandler();
            if (bytes != null && bytes.Length > 0)
            {
                CustomCoverBytes = bytes;
                using var ms = new MemoryStream(bytes);
                CoverImage = new Bitmap(ms);
                CoverImageUrl = string.Empty;
                StatusMessage = "Своя обложка установлена!";
            }
        }
    }

    [RelayCommand]
    public void RemoveCover()
    {
        CustomCoverBytes = null;
        CoverImage = null;
        CustomCoverBytes = null;
        CoverImageUrl = string.Empty;
        StatusMessage = "Обложка очищена.";
    }

    [RelayCommand]
    public async Task ImportTrackAsync()
    {
        if (SelectedVideo == null) return;

        // Stop preview before saving
        _audioPlayerService.Stop();
        IsPlaying = false;

        IsImporting = true;
        ImportProgress = 0;
        LastSavedFilePath = null;

        var tempAudioFile = _cachedPreviewAudioPath;
        var isTempFileCached = !string.IsNullOrEmpty(tempAudioFile) && File.Exists(tempAudioFile);
        var tempTrimmedFile = string.Empty;

        try
        {
            // 1. Download stream if not already cached from preview
            if (!isTempFileCached)
            {
                StatusMessage = "Скачивание аудиопотока...";
                var downloadProgress = new Progress<double>(p =>
                {
                    ImportProgress = p * 40; // 0 to 40%
                });

                tempAudioFile = await _youTubeService.DownloadAudioOnlyStreamAsync(SelectedVideo.Url, downloadProgress);
            }
            else
            {
                ImportProgress = 40;
            }

            // 2. Trim & convert to MP3 via FFmpeg
            StatusMessage = "Нарезка аудио и конвертация в MP3 (320 kbps)...";
            var trimProgress = new Progress<double>(p =>
            {
                ImportProgress = 40 + (p * 40); // 40 to 80%
            });

            var startSpan = TimeSpan.FromSeconds(TrimStartSeconds);
            var endSpan = TimeSpan.FromSeconds(TrimEndSeconds);

            var tempDir = Path.Combine(Path.GetTempPath(), "SpotifyImporter");
            Directory.CreateDirectory(tempDir);
            tempTrimmedFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mp3");

            await _audioProcessingService.TrimAndConvertToMp3Async(
                tempAudioFile!,
                tempTrimmedFile,
                startSpan,
                endSpan,
                trimProgress);

            // 3. Apply ID3 tags
            StatusMessage = "Внедрение ID3v2.3 тегов и обложки...";
            ImportProgress = 85;

            var metadata = new TrackMetadata
            {
                Title = TrackTitle,
                Artist = TrackArtist,
                Album = TrackAlbum,
                Year = TrackYear,
                Genre = TrackGenre,
                CoverUrl = CoverImageUrl,
                CoverData = CustomCoverBytes
            };

            await _metadataService.ApplyMetadataAsync(tempTrimmedFile, metadata);

            // 4. Save to Spotify folder
            StatusMessage = "Сохранение в локальную папку Spotify...";
            ImportProgress = 95;

            _spotifyFolderService.EnsureFolderExists(TargetFolder);
            var fileName = _spotifyFolderService.GenerateSafeFileName(TrackArtist, TrackTitle);
            var finalDestination = Path.Combine(TargetFolder, fileName);

            // Overwrite cleanly if exists
            if (File.Exists(finalDestination))
            {
                File.Delete(finalDestination);
            }
            File.Move(tempTrimmedFile, finalDestination);

            UpdateLocalTracksCount();

            LastSavedFilePath = finalDestination;
            ImportProgress = 100;
            StatusMessage = $"Успешно сохранено: {fileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка импорта: {ex.Message}";
        }
        finally
        {
            // Cleanup trimmed temp file
            try
            {
                if (!string.IsNullOrEmpty(tempTrimmedFile) && File.Exists(tempTrimmedFile))
                    File.Delete(tempTrimmedFile);
            }
            catch { }

            IsImporting = false;
        }
    }

    [RelayCommand]
    public void OpenTargetFolder()
    {
        try
        {
            if (Directory.Exists(TargetFolder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = TargetFolder,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CloseEditor()
    {
        _audioPlayerService.Stop();
        IsPlaying = false;
        HasSelectedVideo = false;
        SelectedVideo = null;
        CoverImage = null;
        CustomCoverBytes = null;
    }

    [RelayCommand]
    public async Task CaptureFromBrowserAsync(string currentUrl)
    {
        if (string.IsNullOrWhiteSpace(currentUrl)) return;

        BrowserStatusText = "Захват видео со страницы...";
        try
        {
            var video = await _youTubeService.GetVideoInfoAsync(currentUrl);
            if (video != null)
            {
                await SelectVideoAsync(video);
                // Switch to Search/Editor tab
                SelectedTabIndex = 0;
                BrowserStatusText = $"Захвачено видео: {video.Title}";
            }
            else
            {
                BrowserStatusText = "Не удалось распознать видео по этой ссылке.";
            }
        }
        catch (Exception ex)
        {
            BrowserStatusText = $"Ошибка захвата: {ex.Message}";
        }
    }
    #endregion

    #region Helper Methods
    private void OnAudioPositionChanged(double seconds)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CurrentPlayheadSeconds = seconds;
            UpdatePlayheadText(seconds);

            // If user reaches end of trim selection, stop or loop
            if (seconds >= TrimEndSeconds && TrimEndSeconds > TrimStartSeconds)
            {
                _audioPlayerService.Seek(TrimStartSeconds);
            }
        });
    }

    private void OnAudioPlaybackEnded()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
        });
    }

    private void UpdatePlayheadText(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        CurrentPlayheadTimeText = span.Hours > 0
            ? span.ToString(@"hh\:mm\:ss\.ff")
            : span.ToString(@"mm\:ss\.ff");
    }

    private async Task LoadCoverImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            CoverImage = null;
        CustomCoverBytes = null;
            return;
        }

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            CoverImage = new Bitmap(ms);
        }
        catch
        {
            CoverImage = null;
        CustomCoverBytes = null;
        }
    }

    private void UpdateTrimTimeTexts()
    {
        var startSpan = TimeSpan.FromSeconds(TrimStartSeconds);
        var endSpan = TimeSpan.FromSeconds(TrimEndSeconds);
        var diffSpan = endSpan > startSpan ? endSpan - startSpan : TimeSpan.Zero;

        TrimStartTimeText = FormatTime(startSpan);
        TrimEndTimeText = FormatTime(endSpan);
        SelectedDurationText = FormatTime(diffSpan);
    }

    private static string FormatTime(TimeSpan span)
    {
        return span.Hours > 0
            ? span.ToString(@"hh\:mm\:ss")
            : span.ToString(@"mm\:ss");
    }
    #endregion
}
