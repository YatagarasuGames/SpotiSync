using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SpotifyVideoImporter.Desktop.ViewModels;

namespace SpotifyVideoImporter.Desktop.Views;

public partial class MainWindow : Window
{
    private NativeWebView? _webView;

    public MainWindow()
    {
        InitializeComponent();
        SetupWebView();
        SetupTimelineEvents();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            vm.PickCoverImageHandler = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Выберите изображение обложки (JPG, PNG)",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
                });

                if (files.Count > 0)
                {
                    await using var stream = await files[0].OpenReadAsync();
                    using var ms = new System.IO.MemoryStream();
                    await stream.CopyToAsync(ms);
                    return ms.ToArray();
                }
                return null;
            };
        }
    }

    private void SetupTimelineEvents()
    {
        if (TimelineControl != null)
        {
            TimelineControl.PositionSeekRequested += sec =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.SeekTo(sec);
                }
            };

            TimelineControl.TrimRangeChanged += (start, end) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.SetTrimRange(start, end);
                }
            };
        }
    }

    private void SetupWebView()
    {
        try
        {
            _webView = new NativeWebView();
            _webView.Source = new Uri("https://www.youtube.com");

            _webView.NavigationCompleted += (sender, e) =>
            {
                if (DataContext is MainViewModel vm && _webView.Source != null)
                {
                    vm.BrowserUrl = _webView.Source.ToString();
                    vm.BrowserStatusText = "Страница загружена";
                }
            };

            WebViewHost.Content = _webView;
        }
        catch (Exception ex)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.BrowserStatusText = $"WebView ошибка инициализации: {ex.Message}";
            }
        }
    }

    private void OnBrowserBackClicked(object? sender, RoutedEventArgs e)
    {
        if (_webView != null && _webView.CanGoBack)
        {
            _webView.GoBack();
        }
    }

    private void OnBrowserForwardClicked(object? sender, RoutedEventArgs e)
    {
        if (_webView != null && _webView.CanGoForward)
        {
            _webView.GoForward();
        }
    }

    private void OnBrowserReloadClicked(object? sender, RoutedEventArgs e)
    {
        _webView?.Refresh();
    }

    private async void OnCaptureVideoClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            string? url = _webView?.Source?.ToString();

            if (_webView != null)
            {
                try
                {
                    var jsUrl = await _webView.InvokeScript("window.location.href");
                    if (!string.IsNullOrWhiteSpace(jsUrl) && jsUrl.StartsWith("http"))
                    {
                        url = jsUrl.Trim('"');
                    }
                }
                catch
                {
                    // Fall back to Source
                }
            }

            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http"))
            {
                url = vm.BrowserUrl;
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                await vm.CaptureFromBrowserAsync(url);
            }
        }
    }
}
