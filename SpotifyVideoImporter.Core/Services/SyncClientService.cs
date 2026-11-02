using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpotifyVideoImporter.Core.Models;

namespace SpotifyVideoImporter.Core.Services;

public class SyncClientService : ISyncClientService
{
    private readonly HttpClient _httpClient;

    public SyncClientService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public async Task<SyncSessionInfo?> DiscoverServerAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        var requestBytes = Encoding.UTF8.GetBytes("DISCOVER_SPOTIFY_SYNC");
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, 5056);

        // Send discovery probe
        try
        {
            await udp.SendAsync(requestBytes, requestBytes.Length, broadcastEndpoint);
        }
        catch { }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var receiveTask = udp.ReceiveAsync(cts.Token).AsTask();
                var result = await receiveTask;
                var text = Encoding.UTF8.GetString(result.Buffer);

                if (text.StartsWith("SPOTIFY_SYNC_OFFER:"))
                {
                    var json = text["SPOTIFY_SYNC_OFFER:".Length..];
                    var session = JsonSerializer.Deserialize<SyncSessionInfo>(json);
                    if (session != null)
                    {
                        // Use remote endpoint IP if IP in session is loopback or empty
                        if (string.IsNullOrEmpty(session.Ip) || session.Ip == "127.0.0.1")
                        {
                            session.Ip = result.RemoteEndPoint.Address.ToString();
                        }
                        return session;
                    }
                }
            }
        }
        catch
        {
            // Timeout or cancellation
        }

        return null;
    }

    public SyncSessionInfo? ParseQrPayload(string qrData)
    {
        if (string.IsNullOrWhiteSpace(qrData)) return null;

        qrData = qrData.Trim();

        // 1. Try parsing JSON
        if (qrData.StartsWith("{") && qrData.EndsWith("}"))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<SyncSessionInfo>(qrData);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Ip))
                {
                    return parsed;
                }
            }
            catch { }
        }

        // 2. Try URI schemes: spotisync://, spotifyimporter://, http://, https://
        if (Uri.TryCreate(qrData, UriKind.Absolute, out var uri))
        {
            var query = ParseQueryString(uri.Query);

            var ip = query.TryGetValue("ip", out var qIp) ? qIp : null;
            if (string.IsNullOrWhiteSpace(ip) && !string.IsNullOrWhiteSpace(uri.Host) && !uri.Host.Equals("sync", StringComparison.OrdinalIgnoreCase))
            {
                ip = uri.Host;
            }

            var port = 5055;
            if (query.TryGetValue("port", out var qPort) && int.TryParse(qPort, out var parsedPort))
            {
                port = parsedPort;
            }
            else if (uri.Port > 0 && uri.Port != 80 && uri.Port != 443)
            {
                port = uri.Port;
            }

            var token = query.TryGetValue("token", out var qToken) ? qToken : string.Empty;
            var name = query.TryGetValue("name", out var qName) ? qName : (!string.IsNullOrWhiteSpace(ip) ? ip : "PC");

            if (!string.IsNullOrWhiteSpace(ip) && IsValidSyncHost(ip) && port is > 0 and <= 65535)
            {
                return new SyncSessionInfo
                {
                    Ip = ip,
                    Port = port,
                    Token = token,
                    Name = name
                };
            }
        }

        // 3. Try parsing IP:Port or IP
        var parts = qrData.Split(':');
        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
        {
            var ip = parts[0].Trim();
            var port = 5055;
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var parsedPort))
            {
                port = parsedPort;
            }

            if (IsValidSyncHost(ip) && port is > 0 and <= 65535)
            {
                return new SyncSessionInfo
                {
                    Ip = ip,
                    Port = port,
                    Name = ip
                };
            }
        }

        return null;
    }

    private static bool IsValidSyncHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 255) return false;
        // Block cloud metadata SSRF attempts
        if (host.StartsWith("169.254.", StringComparison.OrdinalIgnoreCase)) return false;
        if (host.Contains('/') || host.Contains('\\') || host.Contains('\0') || host.Contains(' ')) return false;
        return true;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return dict;

        var trimmed = query.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
            dict[key] = val;
        }
        return dict;
    }

    public async Task<bool> PingServerAsync(SyncSessionInfo session, CancellationToken ct = default)
    {
        try
        {
            var url = $"{session.BaseUrl}/api/sync/info";
            using var resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return false;

            try
            {
                var content = await resp.Content.ReadAsStringAsync(ct);
                var info = JsonSerializer.Deserialize<SyncSessionInfo>(content);
                if (info != null)
                {
                    if (string.IsNullOrEmpty(session.Token) && !string.IsNullOrEmpty(info.Token))
                    {
                        session.Token = info.Token;
                    }
                    if (!string.IsNullOrEmpty(info.Name) && (string.IsNullOrEmpty(session.Name) || session.Name == session.Ip))
                    {
                        session.Name = info.Name;
                    }
                }
            }
            catch { }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SyncTrackDto>> GetTracksAsync(SyncSessionInfo session, CancellationToken ct = default)
    {
        var url = $"{session.BaseUrl}/api/sync/tracks?token={Uri.EscapeDataString(session.Token)}";
        var tracks = await _httpClient.GetFromJsonAsync<List<SyncTrackDto>>(url, ct);
        return tracks ?? new List<SyncTrackDto>();
    }

    public async Task DownloadTrackAsync(
        SyncSessionInfo session,
        SyncTrackDto track,
        string targetFolder,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        var encodedName = Uri.EscapeDataString(track.FileName);
        var url = $"{session.BaseUrl}/api/sync/download/{encodedName}?token={Uri.EscapeDataString(session.Token)}";

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? track.FileSizeBytes;
        var destinationPath = Path.Combine(targetFolder, track.FileName);
        var tempDestination = $"{destinationPath}.tmp";

        await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
        await using (var fileStream = new FileStream(tempDestination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long bytesReadTotal = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                bytesReadTotal += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report((double)bytesReadTotal / totalBytes);
                }
            }
        }

        // Replace final file
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }
        File.Move(tempDestination, destinationPath);

        progress?.Report(1.0);
    }

    public IReadOnlyList<SyncTrackDto> MarkExistingTracks(IReadOnlyList<SyncTrackDto> serverTracks, string localFolder)
    {
        if (!Directory.Exists(localFolder))
        {
            return serverTracks;
        }

        foreach (var track in serverTracks)
        {
            var localPath = Path.Combine(localFolder, track.FileName);
            if (File.Exists(localPath))
            {
                var fi = new FileInfo(localPath);
                if (Math.Abs(fi.Length - track.FileSizeBytes) < 1024)
                {
                    track.IsAlreadySynced = true;
                }
            }
        }

        return serverTracks;
    }
}
