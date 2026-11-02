using System.Text.Json.Serialization;

namespace SpotifyVideoImporter.Core.Models;

public class SyncSessionInfo
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 5055;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "PC";

    [JsonIgnore]
    public string BaseUrl => $"http://{Ip}:{Port}";
}
