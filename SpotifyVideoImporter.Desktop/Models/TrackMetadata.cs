using System;

namespace SpotifyVideoImporter.Desktop.Models;

public class TrackMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public uint Year { get; set; } = (uint)DateTime.Now.Year;
    public string Genre { get; set; } = "Music";
    public string? CoverUrl { get; set; }
    public byte[]? CoverData { get; set; }
}
