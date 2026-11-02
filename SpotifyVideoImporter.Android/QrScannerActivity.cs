using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using ZXing;
using AndroidCamera = global::Android.Hardware.Camera;
using AppResult = global::Android.App.Result;
using GradientDrawable = global::Android.Graphics.Drawables.GradientDrawable;

namespace SpotifyVideoImporter.Android;

[Activity(
    Label = "Сканирование QR-кода",
    Theme = "@style/MyTheme.NoActionBar",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class QrScannerActivity : Activity, ISurfaceHolderCallback, AndroidCamera.IPreviewCallback
{
    private const int RequestCameraPermission = 101;
    private const int RequestGalleryImage = 102;

    private AndroidCamera? _camera;
    private SurfaceView? _surfaceView;
    private ISurfaceHolder? _surfaceHolder;
    private QrViewfinderView? _viewfinder;
    private volatile bool _isScanning = true;
    private volatile bool _isProcessingFrame = false;
    private BarcodeReaderGeneric _barcodeReader = null!;
    private bool _isTorchOn = false;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _barcodeReader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true
            }
        };

        var root = new FrameLayout(this)
        {
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        root.SetBackgroundColor(Color.Black);

        _surfaceView = new SurfaceView(this)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        _surfaceHolder = _surfaceView.Holder;
        _surfaceHolder.AddCallback(this);
        root.AddView(_surfaceView);

        _viewfinder = new QrViewfinderView(this)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        root.AddView(_viewfinder);

        // Header Title
        var title = new TextView(this)
        {
            Text = "Наведите камеру на QR-код на ПК",
            TextSize = 13,
            Gravity = GravityFlags.Center,
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
                TopMargin = (int)(44 * Resources!.DisplayMetrics!.Density)
            }
        };
        title.SetTextColor(Color.White);
        title.SetPadding(
            (int)(18 * Resources.DisplayMetrics.Density),
            (int)(9 * Resources.DisplayMetrics.Density),
            (int)(18 * Resources.DisplayMetrics.Density),
            (int)(9 * Resources.DisplayMetrics.Density));
        var bgShape = new GradientDrawable();
        bgShape.SetColor(Color.Argb(200, 24, 24, 24));
        bgShape.SetCornerRadius(20 * Resources.DisplayMetrics.Density);
        title.Background = bgShape;
        root.AddView(title);

        // Bottom action bar
        var bottomBar = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.Bottom | GravityFlags.CenterHorizontal,
                BottomMargin = (int)(36 * Resources.DisplayMetrics.Density)
            }
        };
        bottomBar.SetGravity(GravityFlags.Center);

        // Flashlight toggle button
        var flashBtn = new Button(this)
        {
            Text = "💡 Фонарик",
            TextSize = 12,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                RightMargin = (int)(10 * Resources.DisplayMetrics.Density)
            }
        };
        flashBtn.SetTextColor(Color.White);
        var flashBg = new GradientDrawable();
        flashBg.SetColor(Color.Rgb(36, 36, 36));
        flashBg.SetCornerRadius(22 * Resources.DisplayMetrics.Density);
        flashBtn.Background = flashBg;
        flashBtn.SetPadding(
            (int)(16 * Resources.DisplayMetrics.Density),
            (int)(10 * Resources.DisplayMetrics.Density),
            (int)(16 * Resources.DisplayMetrics.Density),
            (int)(10 * Resources.DisplayMetrics.Density));
        flashBtn.Click += (s, e) => ToggleFlashlight();
        bottomBar.AddView(flashBtn);

        // Gallery picker button
        var galleryBtn = new Button(this)
        {
            Text = "🖼️ Галерея",
            TextSize = 12,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                RightMargin = (int)(10 * Resources.DisplayMetrics.Density)
            }
        };
        galleryBtn.SetTextColor(Color.White);
        var galBg = new GradientDrawable();
        galBg.SetColor(Color.Rgb(36, 36, 36));
        galBg.SetCornerRadius(22 * Resources.DisplayMetrics.Density);
        galleryBtn.Background = galBg;
        galleryBtn.SetPadding(
            (int)(16 * Resources.DisplayMetrics.Density),
            (int)(10 * Resources.DisplayMetrics.Density),
            (int)(16 * Resources.DisplayMetrics.Density),
            (int)(10 * Resources.DisplayMetrics.Density));
        galleryBtn.Click += (s, e) => PickFromGallery();
        bottomBar.AddView(galleryBtn);

        // Cancel button
        var cancelBtn = new Button(this)
        {
            Text = "✕ Закрыть",
            TextSize = 12,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
        };
        cancelBtn.SetTextColor(Color.Rgb(255, 100, 100));
        var cancelBg = new GradientDrawable();
        cancelBg.SetColor(Color.Rgb(36, 36, 36));
        cancelBg.SetCornerRadius(22 * Resources.DisplayMetrics.Density);
        cancelBtn.Background = cancelBg;
        cancelBtn.SetPadding(
            (int)(16 * Resources.DisplayMetrics.Density),
            (int)(10 * Resources.DisplayMetrics.Density),
            (int)(16 * Resources.DisplayMetrics.Density),
            (int)(10 * Resources.DisplayMetrics.Density));
        cancelBtn.Click += (s, e) => Finish();
        bottomBar.AddView(cancelBtn);

        root.AddView(bottomBar);

        SetContentView(root);

        // Check camera permission
        if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted)
        {
            RequestPermissions(new[] { Manifest.Permission.Camera }, RequestCameraPermission);
        }
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        _surfaceHolder = holder;
        StartCameraPreview();
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        StopCamera();
    }

    private void StartCameraPreview()
    {
        if (CheckSelfPermission(Manifest.Permission.Camera) != Permission.Granted)
            return;

        try
        {
            _camera = AndroidCamera.Open();
            _camera.SetDisplayOrientation(90);

            var parameters = _camera.GetParameters();
            if (parameters.SupportedFocusModes?.Contains(AndroidCamera.Parameters.FocusModeContinuousPicture) == true)
            {
                parameters.FocusMode = AndroidCamera.Parameters.FocusModeContinuousPicture;
            }

            _camera.SetParameters(parameters);
            _camera.SetPreviewDisplay(_surfaceHolder);
            _camera.SetPreviewCallback(this);
            _camera.StartPreview();
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, "Камера недоступна: " + ex.Message, ToastLength.Long)?.Show();
        }
    }

    private void StopCamera()
    {
        try
        {
            _camera?.SetPreviewCallback(null);
            _camera?.StopPreview();
            _camera?.Release();
            _camera = null;
        }
        catch { }
    }

    private void ToggleFlashlight()
    {
        if (_camera == null) return;
        try
        {
            var p = _camera.GetParameters();
            if (p.SupportedFlashModes?.Contains(AndroidCamera.Parameters.FlashModeTorch) == true)
            {
                _isTorchOn = !_isTorchOn;
                p.FlashMode = _isTorchOn ? AndroidCamera.Parameters.FlashModeTorch : AndroidCamera.Parameters.FlashModeOff;
                _camera.SetParameters(p);
            }
        }
        catch { }
    }

    public void OnPreviewFrame(byte[]? data, AndroidCamera? camera)
    {
        if (!_isScanning || _isProcessingFrame || data == null || camera == null)
            return;

        _isProcessingFrame = true;

        Task.Run(() =>
        {
            try
            {
                var parameters = camera.GetParameters();
                var size = parameters.PreviewSize;
                if (size != null)
                {
                    var source = new PlanarYUVLuminanceSource(data, size.Width, size.Height, 0, 0, size.Width, size.Height, false);
                    var result = _barcodeReader.Decode(source);
                    if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        _isScanning = false;
                        RunOnUiThread(() =>
                        {
                            ReturnQrResult(result.Text);
                        });
                        return;
                    }
                }
            }
            catch { }
            finally
            {
                _isProcessingFrame = false;
            }
        });
    }

    private void PickFromGallery()
    {
        try
        {
            var intent = new Intent(Intent.ActionGetContent);
            intent.SetType("image/*");
            StartActivityForResult(Intent.CreateChooser(intent, "Выберите фото QR-кода"), RequestGalleryImage);
        }
        catch (Exception ex)
        {
            Toast.MakeText(this, "Ошибка открытия галереи: " + ex.Message, ToastLength.Short)?.Show();
        }
    }

    protected override void OnActivityResult(int requestCode, AppResult resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == RequestGalleryImage && resultCode == AppResult.Ok && data?.Data != null)
        {
            try
            {
                using var stream = ContentResolver?.OpenInputStream(data.Data);
                if (stream != null)
                {
                    var bitmap = BitmapFactory.DecodeStream(stream);
                    if (bitmap != null)
                    {
                        int width = bitmap.Width;
                        int height = bitmap.Height;
                        var pixels = new int[width * height];
                        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

                        var luminance = new byte[width * height];
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            int p = pixels[i];
                            int r = (p >> 16) & 0xFF;
                            int g = (p >> 8) & 0xFF;
                            int b = p & 0xFF;
                            luminance[i] = (byte)((r + g + b) / 3);
                        }

                        var source = new PlanarYUVLuminanceSource(luminance, width, height, 0, 0, width, height, false);
                        var result = _barcodeReader.Decode(source);
                        if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                        {
                            ReturnQrResult(result.Text);
                            return;
                        }
                    }
                }
            }
            catch { }

            Toast.MakeText(this, "QR-код не найден на изображении", ToastLength.Short)?.Show();
        }
    }

    private void ReturnQrResult(string qrData)
    {
        var intent = new Intent();
        intent.PutExtra("qr_data", qrData);
        SetResult(AppResult.Ok, intent);
        Finish();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == RequestCameraPermission)
        {
            if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                StartCameraPreview();
            }
            else
            {
                Toast.MakeText(this, "Для сканирования QR-кода необходим доступ к камере", ToastLength.Long)?.Show();
                Finish();
            }
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        StopCamera();
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_surfaceHolder != null)
        {
            StartCameraPreview();
        }
    }
}
