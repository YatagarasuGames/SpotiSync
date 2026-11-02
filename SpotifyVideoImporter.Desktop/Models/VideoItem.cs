using System;

namespace SpotifyVideoImporter.Desktop.Models;

public class VideoItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    public string FormattedDuration => Duration.Hours > 0
        ? Duration.ToString(@"hh\:mm\:ss")
        : Duration.ToString(@"mm\:ss");
}
