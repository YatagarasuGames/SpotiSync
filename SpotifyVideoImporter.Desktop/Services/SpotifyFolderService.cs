using System;
using System.IO;
using System.Linq;

namespace SpotifyVideoImporter.Desktop.Services;

public interface ISpotifyFolderService
{
    string GetDefaultSpotifyFolder();
    bool EnsureFolderExists(string path);
    string GenerateSafeFileName(string artist, string title);
}

public class SpotifyFolderService : ISpotifyFolderService
{
    public string GetDefaultSpotifyFolder()
    {
        var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        var spotifyFolder = Path.Combine(musicFolder, "Spotify");
        EnsureFolderExists(spotifyFolder);
        return spotifyFolder;
    }

    public bool EnsureFolderExists(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GenerateSafeFileName(string artist, string title)
    {
        var raw = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(raw.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = $"Track_{DateTime.Now.Ticks}";
        }
        return $"{safe}.mp3";
    }
}
