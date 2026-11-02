using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SpotifyVideoImporter.Desktop.Controls;

public class VegasTimelineControl : Control
{
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<VegasTimelineControl, double>(nameof(Duration), 100.0);

    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<VegasTimelineControl, double>(nameof(Position), 0.0);

    public static readonly StyledProperty<double> TrimStartProperty =
        AvaloniaProperty.Register<VegasTimelineControl, double>(nameof(TrimStart), 0.0);

    public static readonly StyledProperty<double> TrimEndProperty =
        AvaloniaProperty.Register<VegasTimelineControl, double>(nameof(TrimEnd), 100.0);

    public static readonly StyledProperty<float[]?> WaveformProperty =
        AvaloniaProperty.Register<VegasTimelineControl, float[]?>(nameof(Waveform));

    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public double Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double TrimStart
    {
        get => GetValue(TrimStartProperty);
        set => SetValue(TrimStartProperty, value);
    }

    public double TrimEnd
    {
        get => GetValue(TrimEndProperty);
        set => SetValue(TrimEndProperty, value);
    }

    public float[]? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public event Action<double>? PositionSeekRequested;
    public event Action<double, double>? TrimRangeChanged;

    private enum DragMode { None, Playhead, TrimStart, TrimEnd }
    private DragMode _currentDrag = DragMode.None;

    private const double RulerHeight = 22.0;
    private const double HandleWidth = 10.0;

    static VegasTimelineControl()
    {
        AffectsRender<VegasTimelineControl>(DurationProperty, PositionProperty, TrimStartProperty, TrimEndProperty, WaveformProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= RulerHeight || Duration <= 0) return;

        var trackTop = RulerHeight;
        var trackHeight = height - RulerHeight;

        // 1. Draw Track Background (Vegas Dark Charcoal)
        var bgBrush = new SolidColorBrush(Color.Parse("#1A1A1A"));
        context.FillRectangle(bgBrush, new Rect(0, trackTop, width, trackHeight));

        // 2. Draw Ruler Background
        var rulerBgBrush = new SolidColorBrush(Color.Parse("#252525"));
        context.FillRectangle(rulerBgBrush, new Rect(0, 0, width, RulerHeight));

        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#383838")), 1);
        context.DrawLine(borderPen, new Point(0, RulerHeight), new Point(width, RulerHeight));

        // 3. Draw Ruler Time Ticks & Labels
        DrawRulerTicks(context, width);

        // 4. Draw Center Track Guideline
        var midY = trackTop + (trackHeight / 2.0);
        var centerLinePen = new Pen(new SolidColorBrush(Color.Parse("#2D2D2D")), 1);
        context.DrawLine(centerLinePen, new Point(0, midY), new Point(width, midY));

        // 5. Draw Waveform Peaks
        DrawWaveform(context, width, trackTop, trackHeight, midY);

        // 6. Draw Dimmed Unselected Regions & Active Selection Area
        var startX = Math.Clamp((TrimStart / Duration) * width, 0, width);
        var endX = Math.Clamp((TrimEnd / Duration) * width, 0, width);
        if (endX < startX) endX = startX;

        // Left Dimmed (before TrimStart)
        if (startX > 0)
        {
            var dimBrush = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            context.FillRectangle(dimBrush, new Rect(0, trackTop, startX, trackHeight));
        }

        // Right Dimmed (after TrimEnd)
        if (endX < width)
        {
            var dimBrush = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            context.FillRectangle(dimBrush, new Rect(endX, trackTop, width - endX, trackHeight));
        }

        // Active Selection Highlight (Vegas Style glowing tint)
        var selectionTint = new SolidColorBrush(Color.FromArgb(35, 29, 185, 84));
        context.FillRectangle(selectionTint, new Rect(startX, trackTop, endX - startX, trackHeight));

        // Active Selection Top Bar
        var selectionTopBar = new SolidColorBrush(Color.Parse("#1DB954"));
        context.FillRectangle(selectionTopBar, new Rect(startX, trackTop, endX - startX, 3));

        // 7. Draw In & Out Markers (Vegas Yellow/Green Brackets)
        DrawTrimHandles(context, startX, endX, trackTop, trackHeight);

        // 8. Draw Playhead (Orange/White Vegas Cursor)
        var playheadX = Math.Clamp((Position / Duration) * width, 0, width);
        DrawPlayhead(context, playheadX, height);
    }

    private void DrawRulerTicks(DrawingContext context, double width)
    {
        // Choose tick intervals based on total duration
        double intervalSec = 10;
        if (Duration > 600) intervalSec = 60;
        else if (Duration > 300) intervalSec = 30;
        else if (Duration > 120) intervalSec = 15;
        else if (Duration > 60) intervalSec = 10;
        else intervalSec = 5;

        var tickPen = new Pen(new SolidColorBrush(Color.Parse("#555555")), 1);
        var majorTickPen = new Pen(new SolidColorBrush(Color.Parse("#888888")), 1);
        var labelBrush = new SolidColorBrush(Color.Parse("#AAAAAA"));

        for (double sec = 0; sec <= Duration; sec += intervalSec)
        {
            var x = (sec / Duration) * width;
            context.DrawLine(majorTickPen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));

            var timeText = TimeSpan.FromSeconds(sec).ToString(Duration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
            var formattedText = new FormattedText(
                timeText,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Inter, sans-serif"),
                9.0,
                labelBrush);

            var textX = Math.Clamp(x + 3, 2, width - formattedText.Width - 2);
            context.DrawText(formattedText, new Point(textX, 4));
        }
    }

    private void DrawWaveform(DrawingContext context, double width, double trackTop, double trackHeight, double midY)
    {
        var wavePen = new Pen(new SolidColorBrush(Color.Parse("#23914B")), 1.5);
        var maxAmplitude = (trackHeight / 2.0) * 0.85;

        var data = Waveform;
        if (data != null && data.Length > 0)
        {
            var step = (double)width / data.Length;
            for (int i = 0; i < data.Length; i++)
            {
                var x = i * step;
                var h = Math.Clamp(data[i] * maxAmplitude, 2, maxAmplitude);
                context.DrawLine(wavePen, new Point(x, midY - h), new Point(x, midY + h));
            }
        }
        else
        {
            // Procedural pseudo-waveform preview while loading audio
            var step = 4.0;
            int count = (int)(width / step);
            for (int i = 0; i < count; i++)
            {
                var x = i * step;
                // Generate natural-looking music energy profile with waves
                var t = (double)i / count;
                var baseWave = Math.Sin(i * 0.15) * 0.4 + Math.Sin(i * 0.05) * 0.3 + 0.3;
                var energy = Math.Abs(baseWave);
                var h = Math.Clamp(energy * maxAmplitude, 3, maxAmplitude);
                context.DrawLine(wavePen, new Point(x, midY - h), new Point(x, midY + h));
            }
        }
    }

    private void DrawTrimHandles(DrawingContext context, double startX, double endX, double trackTop, double trackHeight)
    {
        var handleBrush = new SolidColorBrush(Color.Parse("#FFC83B")); // Vegas Yellow/Amber
        var handlePen = new Pen(handleBrush, 2);

        // In-Marker Bracket '['
        context.DrawLine(handlePen, new Point(startX, trackTop), new Point(startX, trackTop + trackHeight));
        context.DrawLine(handlePen, new Point(startX, trackTop), new Point(startX + 8, trackTop));
        context.DrawLine(handlePen, new Point(startX, trackTop + trackHeight), new Point(startX + 8, trackTop + trackHeight));

        // In-Marker Flag at top
        var flagPathLeft = new PathGeometry();
        using (var fig = flagPathLeft.Open())
        {
            fig.BeginFigure(new Point(startX, 0), true);
            fig.LineTo(new Point(startX + 8, 0));
            fig.LineTo(new Point(startX + 8, RulerHeight));
            fig.LineTo(new Point(startX, RulerHeight));
            fig.EndFigure(true);
        }
        context.DrawGeometry(handleBrush, null, flagPathLeft);

        // Out-Marker Bracket ']'
        context.DrawLine(handlePen, new Point(endX, trackTop), new Point(endX, trackTop + trackHeight));
        context.DrawLine(handlePen, new Point(endX, trackTop), new Point(endX - 8, trackTop));
        context.DrawLine(handlePen, new Point(endX, trackTop + trackHeight), new Point(endX - 8, trackTop + trackHeight));

        // Out-Marker Flag at top
        var flagPathRight = new PathGeometry();
        using (var fig = flagPathRight.Open())
        {
            fig.BeginFigure(new Point(endX, 0), true);
            fig.LineTo(new Point(endX - 8, 0));
            fig.LineTo(new Point(endX - 8, RulerHeight));
            fig.LineTo(new Point(endX, RulerHeight));
            fig.EndFigure(true);
        }
        context.DrawGeometry(handleBrush, null, flagPathRight);
    }

    private void DrawPlayhead(DrawingContext context, double x, double totalHeight)
    {
        var playheadBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
        var playheadPen = new Pen(playheadBrush, 1.5);

        // Vertical cursor line across whole control
        context.DrawLine(playheadPen, new Point(x, 0), new Point(x, totalHeight));

        // Playhead diamond / triangle cursor cap at top
        var cap = new PathGeometry();
        using (var fig = cap.Open())
        {
            fig.BeginFigure(new Point(x - 6, 0), true);
            fig.LineTo(new Point(x + 6, 0));
            fig.LineTo(new Point(x + 6, 8));
            fig.LineTo(new Point(x, 14));
            fig.LineTo(new Point(x - 6, 8));
            fig.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.Parse("#FF4A4A")), null, cap);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        var width = Bounds.Width;
        if (width <= 0 || Duration <= 0) return;

        var startX = (TrimStart / Duration) * width;
        var endX = (TrimEnd / Duration) * width;

        if (Math.Abs(pos.X - startX) <= HandleWidth)
        {
            _currentDrag = DragMode.TrimStart;
            e.Pointer.Capture(this);
        }
        else if (Math.Abs(pos.X - endX) <= HandleWidth)
        {
            _currentDrag = DragMode.TrimEnd;
            e.Pointer.Capture(this);
        }
        else
        {
            _currentDrag = DragMode.Playhead;
            e.Pointer.Capture(this);
            var seekSec = Math.Clamp((pos.X / width) * Duration, 0, Duration);
            Position = seekSec;
            PositionSeekRequested?.Invoke(seekSec);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pos = e.GetPosition(this);
        var width = Bounds.Width;
        if (width <= 0 || Duration <= 0) return;

        var currentSec = Math.Clamp((pos.X / width) * Duration, 0, Duration);

        if (_currentDrag == DragMode.Playhead)
        {
            Position = currentSec;
            PositionSeekRequested?.Invoke(currentSec);
        }
        else if (_currentDrag == DragMode.TrimStart)
        {
            if (currentSec < TrimEnd)
            {
                TrimStart = currentSec;
                TrimRangeChanged?.Invoke(TrimStart, TrimEnd);
            }
        }
        else if (_currentDrag == DragMode.TrimEnd)
        {
            if (currentSec > TrimStart)
            {
                TrimEnd = currentSec;
                TrimRangeChanged?.Invoke(TrimStart, TrimEnd);
            }
        }
        else
        {
            // Cursor icon feedback
            var startX = (TrimStart / Duration) * width;
            var endX = (TrimEnd / Duration) * width;

            if (Math.Abs(pos.X - startX) <= HandleWidth || Math.Abs(pos.X - endX) <= HandleWidth)
            {
                Cursor = new Cursor(StandardCursorType.SizeWestEast);
            }
            else
            {
                Cursor = new Cursor(StandardCursorType.Cross);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _currentDrag = DragMode.None;
        e.Pointer.Capture(null);
    }
}
