using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;

namespace SpotifyVideoImporter.Android.Services;

public static class AndroidStorageHelper
{
    public static bool HasStoragePermission()
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                return global::Android.OS.Environment.IsExternalStorageManager;
            }
            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                var context = Application.Context;
                return context.CheckSelfPermission(Manifest.Permission.WriteExternalStorage) == Permission.Granted;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void RequestStoragePermission()
    {
        MainActivity.Instance?.RequestStoragePermissions();
    }

    public static string GetPreferredSpotifyFolder()
    {
        // 1. If has permission, use public Music/Spotify
        try
        {
            var publicMusic = global::Android.OS.Environment.GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryMusic)?.AbsolutePath
                              ?? "/storage/emulated/0/Music";
            var publicSpotify = Path.Combine(publicMusic, "Spotify");

            if (HasStoragePermission())
            {
                if (!Directory.Exists(publicSpotify))
                {
                    Directory.CreateDirectory(publicSpotify);
                }
                return publicSpotify;
            }
        }
        catch { }

        // 2. Fallback to app-specific external music folder (ALWAYS WRITABLE without permissions)
        try
        {
            var appExtMusic = Application.Context.GetExternalFilesDir(global::Android.OS.Environment.DirectoryMusic)?.AbsolutePath;
            if (!string.IsNullOrEmpty(appExtMusic))
            {
                var appSpotify = Path.Combine(appExtMusic, "Spotify");
                if (!Directory.Exists(appSpotify))
                {
                    Directory.CreateDirectory(appSpotify);
                }
                return appSpotify;
            }
        }
        catch { }

        return "/storage/emulated/0/Music/Spotify";
    }

    public static void ScanMediaFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                MediaScannerConnection.ScanFile(
                    Application.Context,
                    new[] { filePath },
                    new[] { "audio/mpeg" },
                    null);
            }
        }
        catch { }
    }
}
