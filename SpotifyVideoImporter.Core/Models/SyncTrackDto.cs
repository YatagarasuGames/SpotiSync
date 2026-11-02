using System;
using System.Text.Json.Serialization;

namespace SpotifyVideoImporter.Core.Models;

public class SyncTrackDto
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("modifiedUtc")]
    public DateTime ModifiedUtc { get; set; }

    [JsonIgnore]
    public bool IsAlreadySynced { get; set; }

    [JsonIgnore]
    public string StatusText => IsAlreadySynced ? "Скачано" : "Новый";

    [JsonIgnore]
    public string FormattedSize => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";

    [JsonIgnore]
    public string FormattedDuration => TimeSpan.FromSeconds(DurationSeconds).ToString(@"mm\:ss");
}
