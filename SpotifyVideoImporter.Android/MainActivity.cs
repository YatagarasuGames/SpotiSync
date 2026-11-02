using System;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace SpotifyVideoImporter.Android;

[Activity(
    Label = "@string/app_name",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "spotisync")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "spotifysync")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "spotifyimporter")]
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Instance { get; private set; }
    public static string? PendingDeepLinkUri { get; set; }
    public const int RequestQrScan = 200;
    public static Action<string>? OnQrScanResult { get; set; }

    public static void LaunchQrScanner(Action<string> onResult)
    {
        if (Instance == null) return;
        OnQrScanResult = onResult;
        var intent = new Intent(Instance, typeof(QrScannerActivity));
        Instance.StartActivityForResult(intent, RequestQrScan);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RequestQrScan && resultCode == Result.Ok && data != null)
        {
            var qr = data.GetStringExtra("qr_data");
            if (!string.IsNullOrWhiteSpace(qr))
            {
                OnQrScanResult?.Invoke(qr);
                OnQrScanResult = null;
            }
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;

        // Permit network operations without StrictMode throwing NetworkOnMainThreadException
        var policy = new StrictMode.ThreadPolicy.Builder().PermitAll().Build();
        StrictMode.SetThreadPolicy(policy);

        base.OnCreate(savedInstanceState);

        RequestStoragePermissions();
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIntent(intent);
    }

    private void HandleIntent(Intent? intent)
    {
        if (intent == null) return;

        var data = intent.DataString;
        if (!string.IsNullOrWhiteSpace(data))
        {
            data = data.Trim();
            if (data.StartsWith("spotisync:", StringComparison.OrdinalIgnoreCase) ||
                data.StartsWith("spotifysync:", StringComparison.OrdinalIgnoreCase) ||
                data.StartsWith("spotifyimporter:", StringComparison.OrdinalIgnoreCase) ||
                data.Contains("spotify-sync", StringComparison.OrdinalIgnoreCase) ||
                data.Contains(":5055", StringComparison.OrdinalIgnoreCase))
            {
                PendingDeepLinkUri = data;

                if (ViewModels.MobileSyncViewModel.ActiveInstance != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = ViewModels.MobileSyncViewModel.ActiveInstance.HandleDeepLinkAsync(data);
                    });
                }
            }
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        Instance = this;
    }

    public void RequestStoragePermissions()
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                if (!global::Android.OS.Environment.IsExternalStorageManager)
                {
                    var intent = new Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
                    intent.SetData(global::Android.Net.Uri.Parse($"package:{PackageName}"));
                    StartActivity(intent);
                }
            }
            else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                if (CheckSelfPermission(Manifest.Permission.WriteExternalStorage) != Permission.Granted ||
                    CheckSelfPermission(Manifest.Permission.ReadExternalStorage) != Permission.Granted)
                {
                    RequestPermissions(new[]
                    {
                        Manifest.Permission.ReadExternalStorage,
                        Manifest.Permission.WriteExternalStorage
                    }, 100);
                }
            }
        }
        catch
        {
            try
            {
                // Fallback to general all files access settings if package URI fails
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    var intent = new Intent(global::Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                    StartActivity(intent);
                }
            }
            catch { }
        }
    }
}
