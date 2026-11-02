using Avalonia;
using Avalonia.Controls;
using SpotifyVideoImporter.Android.Controls;
using SpotifyVideoImporter.Android.ViewModels;

namespace SpotifyVideoImporter.Android.Views;

public partial class MobileSyncView : UserControl
{
    public MobileSyncView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MobileSyncViewModel vm)
        {
            vm.PickCoverImageHandler = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return null;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var timeline = this.FindControl<VegasTimelineControl>("TimelineControl");
        if (timeline != null && DataContext is MobileSyncViewModel vm)
        {
            timeline.PositionSeekRequested += vm.OnPositionSeekRequested;
            timeline.TrimRangeChanged += vm.OnTrimRangeChanged;
        }
    }
}
