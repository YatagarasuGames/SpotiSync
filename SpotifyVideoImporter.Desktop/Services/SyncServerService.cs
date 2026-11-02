using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QRCoder;
using SpotifyVideoImporter.Core.Models;

namespace SpotifyVideoImporter.Desktop.Services;

public class SyncServerService : ISyncServerService
{
    private TcpListener? _tcpListener;
    private UdpClient? _udpDiscovery;
    private CancellationTokenSource? _cts;
    private string _spotifyFolder = string.Empty;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = 5055;
    public string? LocalIpAddress { get; private set; }
    public string? CurrentToken { get; private set; }
    public string? QrPayload { get; private set; }
    public byte[]? QrCodePngBytes { get; private set; }

    public Task StartAsync(string spotifyFolder, int port = 5055)
    {
        Stop();

        _spotifyFolder = spotifyFolder;
        Port = port;
        LocalIpAddress = GetLocalIpAddress();
        CurrentToken = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var sessionInfo = new SyncSessionInfo
        {
            Ip = LocalIpAddress,
            Port = Port,
            Token = CurrentToken,
            Name = Environment.MachineName
        };

        // Standard SpotiSync deep link for phone camera and in-app scanner
        QrPayload = $"spotisync://sync?ip={sessionInfo.Ip}&port={sessionInfo.Port}&token={sessionInfo.Token}&name={Uri.EscapeDataString(sessionInfo.Name)}";
        QrCodePngBytes = GenerateQrCodePng(QrPayload);

        try
        {
            _cts = new CancellationTokenSource();
            _tcpListener = new TcpListener(IPAddress.Any, Port);
            _tcpListener.Start();
            IsRunning = true;

            _ = AcceptClientsLoopAsync(_tcpListener, _cts.Token);
            _ = StartUdpDiscoveryResponderAsync(sessionInfo, _cts.Token);
        }
        catch (Exception)
        {
            try
            {
                Port = port + 1;
                sessionInfo.Port = Port;
                QrPayload = $"spotisync://sync?ip={sessionInfo.Ip}&port={sessionInfo.Port}&token={sessionInfo.Token}&name={Uri.EscapeDataString(sessionInfo.Name)}";
                QrCodePngBytes = GenerateQrCodePng(QrPayload);

                _cts = new CancellationTokenSource();
                _tcpListener = new TcpListener(IPAddress.Any, Port);
                _tcpListener.Start();
                IsRunning = true;

                _ = AcceptClientsLoopAsync(_tcpListener, _cts.Token);
                _ = StartUdpDiscoveryResponderAsync(sessionInfo, _cts.Token);
            }
            catch
            {
                IsRunning = false;
            }
        }

        return Task.CompletedTask;
    }

    private async Task AcceptClientsLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsRunning)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleTcpClientAsync(client, ct), ct);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task StartUdpDiscoveryResponderAsync(SyncSessionInfo sessionInfo, CancellationToken ct)
    {
        try
        {
            _udpDiscovery = new UdpClient(5056);
            while (!ct.IsCancellationRequested)
            {
                var result = await _udpDiscovery.ReceiveAsync(ct);
                var message = Encoding.UTF8.GetString(result.Buffer);
                if (message.StartsWith("DISCOVER_SPOTIFY_SYNC"))
                {
                    // Respond with current session info
                    var responseJson = JsonSerializer.Serialize(sessionInfo);
                    var responseBytes = Encoding.UTF8.GetBytes("SPOTIFY_SYNC_OFFER:" + responseJson);
                    await _udpDiscovery.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                }
            }
        }
        catch
        {
            // UDP discovery is best-effort
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 30000;

            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(requestLine)) return;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;

                var method = parts[0].ToUpperInvariant();
                var rawUrl = parts[1];

                // Read headers until blank line (limit to max 50 headers, max 4096 bytes per header to prevent DoS)
                int headerCount = 0;
                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync(ct)))
                {
                    if (++headerCount > 50 || headerLine.Length > 4096)
                    {
                        await SendResponseAsync(stream, 431, "Request Header Fields Too Large", "text/plain", Array.Empty<byte>());
                        return;
                    }
                }

                if (method == "OPTIONS")
                {
                    await SendResponseAsync(stream, 200, "OK", "text/plain", Array.Empty<byte>());
                    return;
                }

                var uri = new Uri("http://localhost" + rawUrl);
                var path = uri.AbsolutePath;
                var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var token = queryParams["token"];

                if (path.Equals("/api/sync/info", StringComparison.OrdinalIgnoreCase))
                {
                    var info = new SyncSessionInfo
                    {
                        Ip = LocalIpAddress ?? "127.0.0.1",
                        Port = Port,
                        Token = CurrentToken ?? string.Empty, // Return valid session token to local Wi-Fi peers
                        Name = Environment.MachineName
                    };
                    var json = JsonSerializer.Serialize(info);
                    await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                }
                else if (path == "/" || path.Equals("/sync", StringComparison.OrdinalIgnoreCase))
                {
                    var landingHtml = $$"""
                        <!DOCTYPE html>
                        <html>
                        <head>
                            <meta charset="utf-8">
                            <meta name="viewport" content="width=device-width, initial-scale=1">
                            <title>SpotiSync Connect</title>
                            <style>
                                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #121212; color: #fff; text-align: center; padding: 40px 20px; }
                                .btn { display: block; background: #1DB954; color: #000; font-weight: bold; text-decoration: none; padding: 16px 24px; border-radius: 30px; margin: 16px auto; max-width: 300px; font-size: 16px; }
                                .btn-sec { background: #282828; color: #fff; }
                            </style>
                            <script>
                                setTimeout(function() {
                                    window.location.href = "{{QrPayload}}";
                                }, 500);
                            </script>
                        </head>
                        <body>
                            <h2>📲 SpotiSync</h2>
                            <p>Подключение к компьютеру <b>{{Environment.MachineName}}</b></p>
                            <p><a class="btn" href="{{QrPayload}}">▶ Открыть в SpotiSync</a></p>
                        </body>
                        </html>
                        """;
                    await SendResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(landingHtml));
                }
                else if (path.Equals("/api/sync/tracks", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ValidateToken(token))
                    {
                        await SendResponseAsync(stream, 401, "Unauthorized", "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Invalid token\"}"));
                        return;
                    }

                    var tracks = GetTracksList();
                    var json = JsonSerializer.Serialize(tracks);
                    await SendResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                }
                else if (path.StartsWith("/api/sync/download/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ValidateToken(token))
                    {
                        await SendResponseAsync(stream, 401, "Unauthorized", "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Invalid token\"}"));
                        return;
                    }

                    var rawFileName = Uri.UnescapeDataString(path["/api/sync/download/".Length..]);
                    var safeFileName = Path.GetFileName(rawFileName);

                    // Path Traversal Security Checks:
                    if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Contains(".."))
                    {
                        await SendResponseAsync(stream, 400, "Bad Request", "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Invalid filename\"}"));
                        return;
                    }

                    var fullPath = Path.GetFullPath(Path.Combine(_spotifyFolder, safeFileName));
                    var canonicalSpotifyFolder = Path.GetFullPath(_spotifyFolder);

                    if (!fullPath.StartsWith(canonicalSpotifyFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        await SendResponseAsync(stream, 403, "Forbidden", "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Access denied\"}"));
                        return;
                    }

                    var ext = Path.GetExtension(fullPath).ToLowerInvariant();
                    if (ext != ".mp3" && ext != ".m4a" && ext != ".flac" && ext != ".wav" && ext != ".ogg")
                    {
                        await SendResponseAsync(stream, 403, "Forbidden", "application/json", Encoding.UTF8.GetBytes("{\"error\":\"Unsupported file type\"}"));
                        return;
                    }

                    if (!File.Exists(fullPath))
                    {
                        await SendResponseAsync(stream, 404, "Not Found", "application/json", Encoding.UTF8.GetBytes("{\"error\":\"File not found\"}"));
                        return;
                    }

                    await SendFileResponseAsync(stream, fullPath, safeFileName, ct);
                }
                else
                {
                    await SendResponseAsync(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("Not Found"));
                }
            }
            catch
            {
                // Client disconnected or timed out
            }
        }
    }

    private bool ValidateToken(string? clientToken)
    {
        if (string.IsNullOrEmpty(CurrentToken) || string.IsNullOrEmpty(clientToken))
            return false;

        var expected = Encoding.UTF8.GetBytes(CurrentToken);
        var actual = Encoding.UTF8.GetBytes(clientToken);

        if (expected.Length != actual.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private List<SyncTrackDto> GetTracksList()
    {
        var trackList = new List<SyncTrackDto>();

        if (Directory.Exists(_spotifyFolder))
        {
            var files = Directory.GetFiles(_spotifyFolder, "*.mp3");
            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    var dto = new SyncTrackDto
                    {
                        FileName = fi.Name,
                        FileSizeBytes = fi.Length,
                        ModifiedUtc = fi.LastWriteTimeUtc,
                        Title = Path.GetFileNameWithoutExtension(fi.Name),
                        Artist = "Unknown Artist"
                    };

                    try
                    {
                        using var tagFile = TagLib.File.Create(file);
                        if (!string.IsNullOrWhiteSpace(tagFile.Tag.Title))
                            dto.Title = tagFile.Tag.Title;
                        if (tagFile.Tag.Performers != null && tagFile.Tag.Performers.Length > 0)
                            dto.Artist = tagFile.Tag.Performers[0];
                        if (!string.IsNullOrWhiteSpace(tagFile.Tag.Album))
                            dto.Album = tagFile.Tag.Album;
                        dto.DurationSeconds = tagFile.Properties.Duration.TotalSeconds;
                    }
                    catch { }

                    trackList.Add(dto);
                }
                catch { }
            }
        }

        return trackList;
    }

    private static async Task SendResponseAsync(Stream stream, int statusCode, string statusText, string contentType, byte[] body)
    {
        var header = new StringBuilder();
        header.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
        header.AppendLine($"Content-Type: {contentType}");
        header.AppendLine($"Content-Length: {body.Length}");
        header.AppendLine("Access-Control-Allow-Origin: *");
        header.AppendLine("Access-Control-Allow-Methods: GET, OPTIONS");
        header.AppendLine("Access-Control-Allow-Headers: *");
        header.AppendLine("Connection: close");
        header.AppendLine();

        var headerBytes = Encoding.UTF8.GetBytes(header.ToString());
        await stream.WriteAsync(headerBytes);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body);
        }
        await stream.FlushAsync();
    }

    private static async Task SendFileResponseAsync(Stream stream, string filePath, string fileName, CancellationToken ct)
    {
        using var fs = File.OpenRead(filePath);
        var length = fs.Length;

        var header = new StringBuilder();
        header.AppendLine("HTTP/1.1 200 OK");
        header.AppendLine("Content-Type: audio/mpeg");
        header.AppendLine($"Content-Length: {length}");
        header.AppendLine($"Content-Disposition: attachment; filename=\"{Uri.EscapeDataString(fileName)}\"");
        header.AppendLine("Access-Control-Allow-Origin: *");
        header.AppendLine("Access-Control-Allow-Methods: GET, OPTIONS");
        header.AppendLine("Access-Control-Allow-Headers: *");
        header.AppendLine("Connection: close");
        header.AppendLine();

        var headerBytes = Encoding.UTF8.GetBytes(header.ToString());
        await stream.WriteAsync(headerBytes, ct);

        await fs.CopyToAsync(stream, 65536, ct);
        await stream.FlushAsync(ct);
    }

    private static byte[] GenerateQrCodePng(string payload)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(10);
    }

    private static string GetLocalIpAddress()
    {
        string? candidate = null;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            // Prioritize Wireless (Wi-Fi) and Ethernet
            bool isPreferred = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                               ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet;

            var props = ni.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                    {
                        if (isPreferred && !ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                                        && !ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                                        && !ni.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase))
                        {
                            return ip;
                        }
                        candidate ??= ip;
                    }
                }
            }
        }
        return candidate ?? "127.0.0.1";
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _tcpListener?.Stop();
        }
        catch { }
        try
        {
            _udpDiscovery?.Close();
        }
        catch { }
        _tcpListener = null;
        _udpDiscovery = null;
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
