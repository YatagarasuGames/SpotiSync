using System;
using System.Threading.Tasks;
using SpotifyVideoImporter.Core.Models;

namespace SpotifyVideoImporter.Desktop.Services;

public interface ISyncServerService : IDisposable
{
    bool IsRunning { get; }
    int Port { get; }
    string? LocalIpAddress { get; }
    string? CurrentToken { get; }
    string? QrPayload { get; }
    byte[]? QrCodePngBytes { get; }

    Task StartAsync(string spotifyFolder, int port = 5055);
    void Stop();
}
