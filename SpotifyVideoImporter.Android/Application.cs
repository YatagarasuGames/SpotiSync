using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace SpotifyVideoImporter.Android;

[Application(
    Label = "@string/app_name",
    Icon = "@drawable/icon")]
public class Application : AvaloniaAndroidApplication<App>
{
    public Application(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        var policy = new StrictMode.ThreadPolicy.Builder().PermitAll().Build();
        StrictMode.SetThreadPolicy(policy);

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
