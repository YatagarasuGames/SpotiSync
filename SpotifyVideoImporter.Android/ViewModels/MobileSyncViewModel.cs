using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpotifyVideoImporter.Android.Services;
using SpotifyVideoImporter.Core.Models;
using SpotifyVideoImporter.Core.Services;

namespace SpotifyVideoImporter.Android.ViewModels;

public partial class MobileSyncViewModel : ObservableObject
{
    public static MobileSyncViewModel? ActiveInstance { get; private set; }

    private readonly ISyncClientService _syncClient;
    private readonly MobileYouTubeService _youtubeService;
    private readonly IAndroidAudioPlayerService _audioPlayer;
    private SyncSessionInfo? _currentSession;

    public Func<Task<byte[]?>>? PickCoverImageHandler { get; set; }

    public MobileSyncViewModel()
        : this(new SyncClientService(), new MobileYouTubeService(), new AndroidAudioPlayerService())
    {
    }

    public MobileSyncViewModel(
        ISyncClientService syncClient,
        MobileYouTubeService youtubeService,
        IAndroidAudioPlayerService audioPlayer)
    {
        ActiveInstance = this;
        _syncClient = syncClient;
        _youtubeService = youtubeService;
        _audioPlayer = audioPlayer;

        AvailableTracks = new ObservableCollection<SyncTrackDto>();
        SearchResults = new ObservableCollection<YouTubeVideoResult>();

        RefreshStorageStatus();

        _audioPlayer.PositionChanged += pos =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentPosition = pos;
                if (pos >= TrimEnd && IsPlaying)
                {
                    _audioPlayer.Seek(TrimStart);
                }
            });
        };

        _audioPlayer.PlaybackEnded += () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsPlaying = false;
                CurrentPosition = TrimStart;
            });
        };

        // Check if app was opened via deep link / QR code scan
        if (!string.IsNullOrEmpty(MainActivity.PendingDeepLinkUri))
        {
            var deepLink = MainActivity.PendingDeepLinkUri;
            MainActivity.PendingDeepLinkUri = null;
            Dispatcher.UIThread.Post(() => _ = HandleDeepLinkAsync(deepLink));
        }
    }

    public void RefreshStorageStatus()
    {
        HasStoragePermission = AndroidStorageHelper.HasStoragePermission();
        TargetLocalFolder = AndroidStorageHelper.GetPreferredSpotifyFolder();
    }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; } = 0;

    [RelayCommand]
    public void SwitchToEditor() => SelectedTabIndex = 0;

    [RelayCommand]
    public void SwitchToSync() => SelectedTabIndex = 1;

    [ObservableProperty]
    public partial Bitmap? CoverImage { get; set; }

    [ObservableProperty]
    public partial bool HasCoverImage { get; set; }

    [ObservableProperty]
    public partial byte[]? CurrentCoverBytes { get; set; }

    [RelayCommand]
    public async Task PickCustomCoverAsync()
    {
        if (PickCoverImageHandler != null)
        {
            var bytes = await PickCoverImageHandler();
            if (bytes != null && bytes.Length > 0)
            {
                CurrentCoverBytes = bytes;
                using var ms = new MemoryStream(bytes);
                CoverImage = new Bitmap(ms);
                HasCoverImage = true;
                EditorStatusMessage = "Своя обложка установлена!";
            }
        }
    }

    [RelayCommand]
    public void RemoveCover()
    {
        CurrentCoverBytes = null;
        CoverImage = null;
        HasCoverImage = false;
        EditorStatusMessage = "Обложка очищена.";
    }

    [RelayCommand]
    public async Task HandleDeepLinkAsync(string link)
    {
        try
        {
            SelectedTabIndex = 1;
            StatusMessage = "⚡ Подключение к ПК по QR-коду...";

            await ConnectWithPayloadAsync(link);

            if (IsConnected && NewTracksCount > 0)
            {
                StatusMessage = $"Найдено {NewTracksCount} новых треков! Начинаю автоматическую загрузку...";
                await SyncAllPendingAsync();
                StatusMessage = "✅ Синхронизация завершена! Все треки добавлены в Spotify.";
            }
            else if (IsConnected)
            {
                StatusMessage = "✅ Подключено по QR-коду! Все треки на телефоне уже актуальны.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка подключения: {ex.Message}";
        }
    }

    [ObservableProperty]
    public partial bool HasStoragePermission { get; set; } = true;

    [RelayCommand]
    public void RequestStoragePermission()
    {
        AndroidStorageHelper.RequestStoragePermission();
        RefreshStorageStatus();
    }

    // ==========================================
    // 📲 TAB: SYNC WITH PC
    // ==========================================

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string ServerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Готов к подключению. Нажмите 'Авто-поиск ПК' или введите IP.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double SyncProgress { get; set; }

    [ObservableProperty]
    public partial string CurrentTrackName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TargetLocalFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ManualIpInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int NewTracksCount { get; set; }

    public ObservableCollection<SyncTrackDto> AvailableTracks { get; }

    [RelayCommand]
    public void OpenQrScanner()
    {
        MainActivity.LaunchQrScanner((qr) =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await HandleDeepLinkAsync(qr);
            });
        });
    }

    [RelayCommand]
    public void Disconnect()
    {
        _currentSession = null;
        IsConnected = false;
        ServerName = string.Empty;
        ServerUrl = string.Empty;
        AvailableTracks.Clear();
        NewTracksCount = 0;
        StatusMessage = "Отключено от ПК.";
    }

    [RelayCommand]
    public async Task AutoDiscoverAsync()
    {
        IsBusy = true;
        StatusMessage = "Поиск ПК в Wi-Fi сети...";

        try
        {
            var session = await Task.Run(() => _syncClient.DiscoverServerAsync(TimeSpan.FromSeconds(3)));
            if (session != null)
            {
                StatusMessage = $"Найден компьютер: {session.Name} ({session.Ip})";
                await ConnectWithPayloadAsync(session.BaseUrl + "?token=" + session.Token);
            }
            else
            {
                StatusMessage = "Компьютер не найден в сети. Убедитесь, что приложение на ПК открыто во вкладке синхронизации.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка авто-поиска: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ConnectFromInputAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualIpInput)) return;
        await ConnectWithPayloadAsync(ManualIpInput.Trim());
    }

    [RelayCommand]
    public async Task PasteClipboardInputAsync()
    {
        try
        {
            if (MainActivity.Instance != null)
            {
                var cm = MainActivity.Instance.GetSystemService(global::Android.Content.Context.ClipboardService) as global::Android.Content.ClipboardManager;
                if (cm?.PrimaryClip != null && cm.PrimaryClip.ItemCount > 0)
                {
                    var text = cm.PrimaryClip.GetItemAt(0)?.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        ManualIpInput = text.Trim();
                        await ConnectWithPayloadAsync(ManualIpInput);
                        return;
                    }
                }
            }
            StatusMessage = "Буфер обмена пуст или не содержит текста.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка чтения буфера: {ex.Message}";
        }
    }

    public async Task ConnectWithPayloadAsync(string qrOrIpPayload)
    {
        IsBusy = true;
        StatusMessage = "Подключение к ПК...";

        try
        {
            var session = _syncClient.ParseQrPayload(qrOrIpPayload);
            if (session == null)
            {
                StatusMessage = "Некорректный адрес или QR-код.";
                return;
            }

            var pingOk = await Task.Run(() => _syncClient.PingServerAsync(session));
            if (!pingOk)
            {
                StatusMessage = $"Не удалось связаться с {session.BaseUrl}. Проверьте одну ли Wi-Fi сеть используют устройства.";
                return;
            }

            _currentSession = session;
            IsConnected = true;
            ServerName = session.Name;
            ServerUrl = session.BaseUrl;

            await RefreshTracksAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка подключения: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RefreshTracksAsync()
    {
        if (_currentSession == null) return;

        IsBusy = true;
        StatusMessage = "Получение списка треков с ПК...";

        try
        {
            RefreshStorageStatus();
            var serverTracks = await Task.Run(() => _syncClient.GetTracksAsync(_currentSession));
            var marked = _syncClient.MarkExistingTracks(serverTracks, TargetLocalFolder);

            AvailableTracks.Clear();
            foreach (var t in marked)
            {
                AvailableTracks.Add(t);
            }

            NewTracksCount = AvailableTracks.Count(t => !t.IsAlreadySynced);
            StatusMessage = $"Подключено к {ServerName}. Доступно треков: {AvailableTracks.Count} (Новых: {NewTracksCount})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка обновления списка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SyncAllPendingAsync()
    {
        if (_currentSession == null || NewTracksCount == 0) return;

        RefreshStorageStatus();
        if (!HasStoragePermission)
        {
            AndroidStorageHelper.RequestStoragePermission();
        }

        IsBusy = true;
        SyncProgress = 0;

        var pending = AvailableTracks.Where(t => !t.IsAlreadySynced).ToList();
        var total = pending.Count;
        var completed = 0;

        try
        {
            await Task.Run(async () =>
            {
                // Ensure target folder exists or fallback
                try
                {
                    if (!Directory.Exists(TargetLocalFolder))
                    {
                        Directory.CreateDirectory(TargetLocalFolder);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Fallback to app-specific folder if public Music is denied
                    TargetLocalFolder = AndroidStorageHelper.GetPreferredSpotifyFolder();
                    if (!Directory.Exists(TargetLocalFolder))
                    {
                        Directory.CreateDirectory(TargetLocalFolder);
                    }
                }

                foreach (var track in pending)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentTrackName = $"{track.Artist} - {track.Title}";
                        StatusMessage = $"Синхронизация ({completed + 1} из {total}): {track.FileName}...";
                    });

                    var trackProgress = new Progress<double>(p =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            SyncProgress = ((completed + p) / total) * 100.0;
                        });
                    });

                    await _syncClient.DownloadTrackAsync(_currentSession, track, TargetLocalFolder, trackProgress);

                    var downloadedPath = Path.Combine(TargetLocalFolder, track.FileName);
                    AndroidStorageHelper.ScanMediaFile(downloadedPath);

                    completed++;
                    Dispatcher.UIThread.Post(() =>
                    {
                        track.IsAlreadySynced = true;
                        SyncProgress = ((double)completed / total) * 100.0;
                    });
                }
            });

            NewTracksCount = AvailableTracks.Count(t => !t.IsAlreadySynced);
            StatusMessage = $"Успешно сохранено {completed} треков в папку: {TargetLocalFolder}!";
            CurrentTrackName = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка синхронизации: {ex.Message}";
            if (ex is UnauthorizedAccessException || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                AndroidStorageHelper.RequestStoragePermission();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==========================================
    // 🎵 TAB: YOUTUBE DOWNLOAD & VEGAS TIMELINE
    // ==========================================

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    public ObservableCollection<YouTubeVideoResult> SearchResults { get; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool HasAudioLoaded { get; set; }

    [ObservableProperty]
    public partial double AudioDuration { get; set; } = 100.0;

    [ObservableProperty]
    public partial double CurrentPosition { get; set; }

    [ObservableProperty]
    public partial double TrimStart { get; set; }

    [ObservableProperty]
    public partial double TrimEnd { get; set; } = 100.0;

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial string EditorTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorArtist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorAlbum { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditorStatusMessage { get; set; } = "Вставьте ссылку на YouTube или введите название трека:";

    [ObservableProperty]
    public partial bool IsEditorBusy { get; set; }

    [ObservableProperty]
    public partial double EditorProgress { get; set; }

    private string? _downloadedTempAudioPath;
    private byte[]? _cachedArtworkBytes;

    [RelayCommand]
    public async Task SearchYouTubeAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        var directId = MobileYouTubeService.ExtractVideoId(SearchQuery);
        if (!string.IsNullOrEmpty(directId))
        {
            await LoadVideoAsync(new YouTubeVideoResult { VideoId = directId, Title = "YouTube Audio" });
            return;
        }

        IsEditorBusy = true;
        IsSearching = true;
        EditorStatusMessage = $"Поиск '{SearchQuery}'...";
        SearchResults.Clear();

        try
        {
            var results = await Task.Run(() => _youtubeService.SearchVideosAsync(SearchQuery));
            foreach (var r in results)
            {
                SearchResults.Add(r);
            }

            if (results.Count == 0)
            {
                EditorStatusMessage = "Ничего не найдено. Попробуйте другой запрос или ссылку.";
            }
            else
            {
                EditorStatusMessage = $"Найдено результатов: {results.Count}. Выберите видео для загрузки.";
            }
        }
        catch (Exception ex)
        {
            EditorStatusMessage = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsEditorBusy = false;
            IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task LoadVideoAsync(YouTubeVideoResult? item)
    {
        if (item == null) return;

        IsEditorBusy = true;
        EditorProgress = 0;
        EditorStatusMessage = $"Загрузка аудио: {item.Title}...";

        try
        {
            _audioPlayer.Stop();
            IsPlaying = false;

            var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "audio_cache");
            var progress = new Progress<double>(p =>
            {
                Dispatcher.UIThread.Post(() => EditorProgress = p * 100.0);
            });

            var filePath = await Task.Run(() => _youtubeService.DownloadAudioAsync(item.Url, cacheDir, progress));
            _downloadedTempAudioPath = filePath;

            _audioPlayer.Load(filePath);
            var dur = _audioPlayer.DurationSeconds;
            if (dur <= 0) dur = 180.0;

            AudioDuration = dur;
            TrimStart = 0;
            TrimEnd = dur;
            CurrentPosition = 0;
            HasAudioLoaded = true;

            CurrentCoverBytes = null;
            CoverImage = null;
            HasCoverImage = false;

            EditorTitle = item.Title;
            EditorArtist = item.Author;
            EditorAlbum = "Spotify Import";

            if (item.Title.Contains(" - "))
            {
                var parts = item.Title.Split(" - ", 2);
                EditorArtist = parts[0].Trim();
                EditorTitle = parts[1].Trim();
            }

            EditorTitle = EditorTitle
                .Replace("(Official Video)", "", StringComparison.OrdinalIgnoreCase)
                .Replace("(Official Audio)", "", StringComparison.OrdinalIgnoreCase)
                .Replace("(Official Music Video)", "", StringComparison.OrdinalIgnoreCase)
                .Replace("[Official Video]", "", StringComparison.OrdinalIgnoreCase)
                .Replace("[Official Audio]", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            EditorStatusMessage = $"Трек загружен! Длительность: {TimeSpan.FromSeconds(dur):mm\\:ss}. Задайте границы обрезки на таймлайне.";

            _ = SearchITunesAsync();
        }
        catch (Exception ex)
        {
            EditorStatusMessage = $"Ошибка скачивания: {ex.Message}";
        }
        finally
        {
            IsEditorBusy = false;
        }
    }

    [RelayCommand]
    public void TogglePlay()
    {
        if (!HasAudioLoaded) return;

        if (IsPlaying)
        {
            _audioPlayer.Pause();
            IsPlaying = false;
        }
        else
        {
            if (CurrentPosition < TrimStart || CurrentPosition >= TrimEnd)
            {
                _audioPlayer.Seek(TrimStart);
                CurrentPosition = TrimStart;
            }
            _audioPlayer.Play();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    public void SeekToStart()
    {
        if (!HasAudioLoaded) return;
        _audioPlayer.Seek(TrimStart);
        CurrentPosition = TrimStart;
    }

    public void OnPositionSeekRequested(double sec)
    {
        if (!HasAudioLoaded) return;
        _audioPlayer.Seek(sec);
        CurrentPosition = sec;
    }

    public void OnTrimRangeChanged(double start, double end)
    {
        TrimStart = start;
        TrimEnd = end;
    }

    [RelayCommand]
    public async Task SearchITunesAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorTitle)) return;

        var query = $"{EditorArtist} {EditorTitle}".Trim();
        try
        {
            var match = await Task.Run(() => _youtubeService.SearchITunesAsync(query));
            if (match != null)
            {
                if (!string.IsNullOrEmpty(match.TrackName)) EditorTitle = match.TrackName;
                if (!string.IsNullOrEmpty(match.ArtistName)) EditorArtist = match.ArtistName;
                if (!string.IsNullOrEmpty(match.AlbumName)) EditorAlbum = match.AlbumName;
                _cachedArtworkBytes = match.ArtworkBytes;

                if (match.ArtworkBytes != null && match.ArtworkBytes.Length > 0 && CurrentCoverBytes == null)
                {
                    CurrentCoverBytes = match.ArtworkBytes;
                    using var ms = new MemoryStream(match.ArtworkBytes);
                    CoverImage = new Bitmap(ms);
                    HasCoverImage = true;
                }

                EditorStatusMessage = $"Теги найдены в iTunes: {EditorArtist} — {EditorTitle}";
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task SaveTrimmedToSpotifyAsync()
    {
        if (string.IsNullOrEmpty(_downloadedTempAudioPath) || !File.Exists(_downloadedTempAudioPath))
        {
            EditorStatusMessage = "Сначала загрузите трек.";
            return;
        }

        RefreshStorageStatus();
        if (!HasStoragePermission)
        {
            AndroidStorageHelper.RequestStoragePermission();
        }

        IsEditorBusy = true;
        EditorStatusMessage = "Обрезка и сохранение в папку Spotify...";

        try
        {
            _audioPlayer.Pause();
            IsPlaying = false;

            try
            {
                if (!Directory.Exists(TargetLocalFolder))
                {
                    Directory.CreateDirectory(TargetLocalFolder);
                }
            }
            catch (UnauthorizedAccessException)
            {
                TargetLocalFolder = AndroidStorageHelper.GetPreferredSpotifyFolder();
                if (!Directory.Exists(TargetLocalFolder))
                {
                    Directory.CreateDirectory(TargetLocalFolder);
                }
            }

            var safeArtist = string.Join("_", EditorArtist.Split(Path.GetInvalidFileNameChars()));
            var safeTitle = string.Join("_", EditorTitle.Split(Path.GetInvalidFileNameChars()));
            var finalFileName = $"{safeArtist} - {safeTitle}.mp3".TrimStart('-', ' ').Trim();
            if (string.IsNullOrWhiteSpace(finalFileName) || finalFileName == ".mp3")
            {
                finalFileName = $"Track_{DateTime.Now:yyyyMMdd_HHmmss}.mp3";
            }

            var finalPath = Path.Combine(TargetLocalFolder, finalFileName);
            var tempTrimPath = finalPath + ".tmp";

            var coverToApply = CurrentCoverBytes ?? _cachedArtworkBytes;
            await Task.Run(async () =>
            {
                await MobileAudioTrimmerService.TrimMp3LosslessAsync(_downloadedTempAudioPath, tempTrimPath, TrimStart, TrimEnd);
                await MobileAudioTrimmerService.ApplyTagsAsync(tempTrimPath, EditorTitle, EditorArtist, EditorAlbum, coverToApply);
            });

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }
            File.Move(tempTrimPath, finalPath);

            AndroidStorageHelper.ScanMediaFile(finalPath);
            EditorStatusMessage = $"Готово! Сохранено в: {finalPath}";
        }
        catch (Exception ex)
        {
            EditorStatusMessage = $"Ошибка сохранения: {ex.Message}";
            if (ex is UnauthorizedAccessException || ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                AndroidStorageHelper.RequestStoragePermission();
            }
        }
        finally
        {
            IsEditorBusy = false;
        }
    }
}
