using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SpotifyVideoImporter.Android.ViewModels;
using SpotifyVideoImporter.Android.Views;

namespace SpotifyVideoImporter.Android;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Control CreateViewSafe()
        {
            try
            {
                return new MobileSyncView
                {
                    DataContext = new MobileSyncViewModel()
                };
            }
            catch (Exception ex)
            {
                return new ScrollViewer
                {
                    Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                    Content = new TextBlock
                    {
                        Text = $"Startup Error:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                        Foreground = Brushes.IndianRed,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(16)
                    }
                };
            }
        }

        if (ApplicationLifetime is IActivityApplicationLifetime activityApplicationLifetime)
        {
            activityApplicationLifetime.MainViewFactory = CreateViewSafe;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = CreateViewSafe();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
