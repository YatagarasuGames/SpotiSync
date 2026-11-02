using System;
using Android.Content;
using Android.Graphics;
using Android.Views;

namespace SpotifyVideoImporter.Android;

public class QrViewfinderView : View
{
    private readonly Paint _maskPaint;
    private readonly Paint _cornerPaint;
    private readonly Paint _laserPaint;
    private float _laserY;
    private bool _laserMovingDown = true;

    public Rect FramingRect { get; private set; } = new Rect();

    public QrViewfinderView(Context context) : base(context)
    {
        _maskPaint = new Paint
        {
            Color = Color.Argb(160, 0, 0, 0),
            AntiAlias = true
        };

        _cornerPaint = new Paint
        {
            Color = Color.Rgb(29, 185, 84), // Spotify green
            StrokeWidth = 5 * Resources!.DisplayMetrics!.Density,
            AntiAlias = true
        };
        _cornerPaint.SetStyle(Paint.Style.Stroke);
        _cornerPaint.StrokeCap = Paint.Cap.Round;

        _laserPaint = new Paint
        {
            Color = Color.Argb(180, 29, 185, 84),
            StrokeWidth = 2 * Resources.DisplayMetrics.Density,
            AntiAlias = true
        };
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);

        float density = Resources!.DisplayMetrics!.Density;
        int boxSize = (int)Math.Min(w * 0.75f, 280 * density);

        int left = (w - boxSize) / 2;
        int top = (h - boxSize) / 2 - (int)(30 * density);
        int right = left + boxSize;
        int bottom = top + boxSize;

        FramingRect = new Rect(left, top, right, bottom);
        _laserY = top;
    }

    protected override void OnDraw(Canvas? canvas)
    {
        base.OnDraw(canvas);
        if (canvas == null) return;

        int width = Width;
        int height = Height;
        var frame = FramingRect;

        // Draw dark dimmed mask outside the target frame
        canvas.DrawRect(0, 0, width, frame.Top, _maskPaint);
        canvas.DrawRect(0, frame.Top, frame.Left, frame.Bottom, _maskPaint);
        canvas.DrawRect(frame.Right, frame.Top, width, frame.Bottom, _maskPaint);
        canvas.DrawRect(0, frame.Bottom, width, height, _maskPaint);

        // Draw 4 corner brackets
        float cornerLen = 28 * Resources!.DisplayMetrics!.Density;

        // Top-left
        canvas.DrawLine(frame.Left, frame.Top, frame.Left + cornerLen, frame.Top, _cornerPaint);
        canvas.DrawLine(frame.Left, frame.Top, frame.Left, frame.Top + cornerLen, _cornerPaint);

        // Top-right
        canvas.DrawLine(frame.Right, frame.Top, frame.Right - cornerLen, frame.Top, _cornerPaint);
        canvas.DrawLine(frame.Right, frame.Top, frame.Right, frame.Top + cornerLen, _cornerPaint);

        // Bottom-left
        canvas.DrawLine(frame.Left, frame.Bottom, frame.Left + cornerLen, frame.Bottom, _cornerPaint);
        canvas.DrawLine(frame.Left, frame.Bottom, frame.Left, frame.Bottom - cornerLen, _cornerPaint);

        // Bottom-right
        canvas.DrawLine(frame.Right, frame.Bottom, frame.Right - cornerLen, frame.Bottom, _cornerPaint);
        canvas.DrawLine(frame.Right, frame.Bottom, frame.Right, frame.Bottom - cornerLen, _cornerPaint);

        // Animated laser line
        canvas.DrawLine(frame.Left + 10, _laserY, frame.Right - 10, _laserY, _laserPaint);

        float step = 3 * Resources.DisplayMetrics.Density;
        if (_laserMovingDown)
        {
            _laserY += step;
            if (_laserY >= frame.Bottom - 10) _laserMovingDown = false;
        }
        else
        {
            _laserY -= step;
            if (_laserY <= frame.Top + 10) _laserMovingDown = true;
        }

        PostInvalidateDelayed(25);
    }
}
