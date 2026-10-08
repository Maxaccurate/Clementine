using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace ZestDrop;

/// <summary>
/// The crop frame drawn over a preview: drag inside it to move it, drag a corner or side to resize it. The surroundings
/// are dimmed, so what stays visible is what is kept. It always holds a crop; it works in source pixels and maps them onto
/// the picture, which fills the control as large as it fits (the same way the preview image does).
/// </summary>
internal sealed class CropFrame : FrameworkElement
{
    private const double EdgeBand = 12, CornerBand = 24, Outside = 9;
    private static readonly Color AccentColor = ((SolidColorBrush)UiTheme.Accent).Color;
    private static readonly Brush Shade = Frozen(new SolidColorBrush(Color.FromArgb(150, 8, 10, 12)));
    private static readonly Brush LabelBack = Frozen(new SolidColorBrush(Color.FromArgb(185, 20, 22, 24)));
    private static readonly Pen EdgeHalo = MakePen(Color.FromArgb(110, 0, 0, 0), 4), EdgeLine = MakePen(Colors.White, 1.5);
    private static readonly Pen Thirds = MakePen(Color.FromArgb(85, 255, 255, 255), 1);
    private static readonly Pen HandleHalo = MakePen(Color.FromArgb(130, 0, 0, 0), 6.5, true), HandleLine = MakePen(Colors.White, 3.5, true), HandleActive = MakePen(AccentColor, 3.5, true);

    private int sourceWidth, sourceHeight;
    private CropRect crop;
    private CropHandle hover, drag;
    private Point dragOrigin;
    private CropRect dragStart;

    /// <summary>The ratio the frame keeps while it is resized; null means free.</summary>
    public CropRatio? Ratio { get; set; }
    public CropRect Crop => crop;
    public bool HasSource => sourceWidth > 0 && sourceHeight > 0;
    public bool IsDragging => drag != CropHandle.None;
    public int MinSize => CropMath.MinSize(sourceWidth, sourceHeight);

    /// <summary>Raised on every change the user makes by dragging.</summary>
    public event Action? Changed;
    public event Action? DragStarted, DragEnded;

    public CropFrame()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
    }

    /// <summary>Tells the frame how big the picture is; the crop starts as the whole picture.</summary>
    public void SetSource(int width, int height)
    {
        sourceWidth = width;
        sourceHeight = height;
        crop = new CropRect(0, 0, Math.Max(0, width), Math.Max(0, height));
        InvalidateVisual();
    }

    /// <summary>Sets the crop from code (typed values, a reset). Does not raise <see cref="Changed"/>.</summary>
    public void SetCrop(CropRect value)
    {
        crop = value;
        InvalidateVisual();
    }

    // ---- geometry ----------------------------------------------------------------------------------------------------
    private double Scale => HasSource ? Math.Min(ActualWidth / sourceWidth, ActualHeight / sourceHeight) : 0;

    /// <summary>Where the picture sits inside the control.</summary>
    public Rect ImageViewRect
    {
        get
        {
            double scale = Scale, w = sourceWidth * scale, h = sourceHeight * scale;
            return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
        }
    }

    /// <summary>Where the crop sits inside the control.</summary>
    public Rect CropViewRect
    {
        get
        {
            var image = ImageViewRect;
            double scale = Scale;
            return new Rect(image.Left + crop.X * scale, image.Top + crop.Y * scale, crop.Width * scale, crop.Height * scale);
        }
    }

    /// <summary>What a press at this point would grab. Corners win over sides, sides over the inside.</summary>
    public CropHandle HitTest(Point p)
    {
        if (!HasSource || Scale <= 0)
            return CropHandle.None;
        var c = CropViewRect;
        if (p.X < c.Left - Outside || p.X > c.Right + Outside || p.Y < c.Top - Outside || p.Y > c.Bottom + Outside)
            return CropHandle.None;
        double small = Math.Min(c.Width, c.Height), edge = Math.Min(EdgeBand, small / 3), corner = Math.Min(CornerBand, small / 2.5);
        bool nearLeft = p.X <= c.Left + corner, nearRight = p.X >= c.Right - corner, nearTop = p.Y <= c.Top + corner, nearBottom = p.Y >= c.Bottom - corner;
        if (nearTop && nearLeft) return CropHandle.TopLeft;
        if (nearTop && nearRight) return CropHandle.TopRight;
        if (nearBottom && nearLeft) return CropHandle.BottomLeft;
        if (nearBottom && nearRight) return CropHandle.BottomRight;
        if (p.Y <= c.Top + edge) return CropHandle.Top;
        if (p.Y >= c.Bottom - edge) return CropHandle.Bottom;
        if (p.X <= c.Left + edge) return CropHandle.Left;
        if (p.X >= c.Right - edge) return CropHandle.Right;
        return CropHandle.Move;
    }

    // Lets screen readers (and UI tests) find the frame and read its size.
    protected override AutomationPeer OnCreateAutomationPeer() => new FramePeer(this);

    private sealed class FramePeer(CropFrame owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
        protected override string GetClassNameCore() => nameof(CropFrame);
        protected override string GetHelpTextCore() => $"{owner.crop.Width} × {owner.crop.Height} @ {owner.crop.X}, {owner.crop.Y}";
    }

    // Only the frame takes the mouse; everywhere else clicks fall through to whatever is underneath.
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters) =>
        HitTest(parameters.HitPoint) != CropHandle.None ? new PointHitTestResult(this, parameters.HitPoint) : null;

    // ---- dragging (public so tests can drive it without a real mouse) ------------------------------------------------
    public bool BeginDrag(Point p)
    {
        var handle = HitTest(p);
        if (handle == CropHandle.None)
            return false;
        drag = handle;
        dragOrigin = p;
        dragStart = crop;
        DragStarted?.Invoke();
        InvalidateVisual();
        return true;
    }

    public void DragTo(Point p)
    {
        if (!IsDragging || Scale <= 0)
            return;
        double dx = (p.X - dragOrigin.X) / Scale, dy = (p.Y - dragOrigin.Y) / Scale;
        var next = drag == CropHandle.Move
            ? CropMath.Move(dragStart, dx, dy, sourceWidth, sourceHeight)
            : CropMath.Resize(dragStart, drag, dx, dy, sourceWidth, sourceHeight, Ratio, MinSize);
        if (next == crop)
            return;
        crop = next;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void EndDrag()
    {
        if (!IsDragging)
            return;
        drag = CropHandle.None;
        InvalidateVisual();
        DragEnded?.Invoke();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (BeginDrag(e.GetPosition(this)))
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (IsDragging)
            DragTo(p);
        else
            SetHover(HitTest(p));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsDragging)
            return;
        EndDrag();
        ReleaseMouseCapture();
        e.Handled = true;
    }

    // Losing the mouse (alt-tab, a dialog) ends the drag instead of leaving the frame stuck to the pointer.
    protected override void OnLostMouseCapture(MouseEventArgs e) => EndDrag();

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!IsDragging)
            SetHover(CropHandle.None);
    }

    private void SetHover(CropHandle handle)
    {
        if (handle == hover)
            return;
        hover = handle;
        Cursor = handle switch
        {
            CropHandle.Move => Cursors.SizeAll,
            CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
            CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
            CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
            CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
            _ => null
        };
        InvalidateVisual();
    }

    // ---- drawing -----------------------------------------------------------------------------------------------------
    protected override void OnRender(DrawingContext dc)
    {
        if (!HasSource || ActualWidth < 4 || ActualHeight < 4 || Scale <= 0)
            return;
        var image = ImageViewRect;
        var c = CropViewRect;
        if (crop.Width != sourceWidth || crop.Height != sourceHeight)
        {
            dc.DrawRectangle(Shade, null, new Rect(image.Left, image.Top, image.Width, Math.Max(0, c.Top - image.Top)));
            dc.DrawRectangle(Shade, null, new Rect(image.Left, c.Bottom, image.Width, Math.Max(0, image.Bottom - c.Bottom)));
            dc.DrawRectangle(Shade, null, new Rect(image.Left, c.Top, Math.Max(0, c.Left - image.Left), c.Height));
            dc.DrawRectangle(Shade, null, new Rect(c.Right, c.Top, Math.Max(0, image.Right - c.Right), c.Height));
        }
        for (int i = 1; i <= 2; i++)
        {
            dc.DrawLine(Thirds, new Point(c.Left + c.Width * i / 3, c.Top), new Point(c.Left + c.Width * i / 3, c.Bottom));
            dc.DrawLine(Thirds, new Point(c.Left, c.Top + c.Height * i / 3), new Point(c.Right, c.Top + c.Height * i / 3));
        }
        // Drawn a hair inside, so a frame that fills the picture is not cut off by the edge of the control.
        var border = new Rect(c.Left + .75, c.Top + .75, Math.Max(0, c.Width - 1.5), Math.Max(0, c.Height - 1.5));
        dc.DrawRectangle(null, EdgeHalo, border);
        dc.DrawRectangle(null, EdgeLine, border);

        // Corner brackets and side bars sit just inside the border, so none of them are cut off at the picture's edge.
        double arm = Math.Min(20, Math.Min(c.Width, c.Height) / 3), inset = 4;
        Corner(dc, new Point(c.Left + inset, c.Top + inset), 1, 1, arm, CropHandle.TopLeft);
        Corner(dc, new Point(c.Right - inset, c.Top + inset), -1, 1, arm, CropHandle.TopRight);
        Corner(dc, new Point(c.Left + inset, c.Bottom - inset), 1, -1, arm, CropHandle.BottomLeft);
        Corner(dc, new Point(c.Right - inset, c.Bottom - inset), -1, -1, arm, CropHandle.BottomRight);
        double half = Math.Min(13, Math.Min(c.Width, c.Height) / 8);
        if (c.Width > 110)
        {
            Bar(dc, new Point(c.Left + c.Width / 2 - half, c.Top + inset), new Point(c.Left + c.Width / 2 + half, c.Top + inset), CropHandle.Top);
            Bar(dc, new Point(c.Left + c.Width / 2 - half, c.Bottom - inset), new Point(c.Left + c.Width / 2 + half, c.Bottom - inset), CropHandle.Bottom);
        }
        if (c.Height > 110)
        {
            Bar(dc, new Point(c.Left + inset, c.Top + c.Height / 2 - half), new Point(c.Left + inset, c.Top + c.Height / 2 + half), CropHandle.Left);
            Bar(dc, new Point(c.Right - inset, c.Top + c.Height / 2 - half), new Point(c.Right - inset, c.Top + c.Height / 2 + half), CropHandle.Right);
        }

        if (IsDragging)
            DrawSizeLabel(dc, c);
    }

    private void Corner(DrawingContext dc, Point corner, int sx, int sy, double arm, CropHandle handle)
    {
        var figure = new StreamGeometry();
        using (var context = figure.Open())
        {
            context.BeginFigure(new Point(corner.X + sx * arm, corner.Y), false, false);
            context.LineTo(corner, true, true);
            context.LineTo(new Point(corner.X, corner.Y + sy * arm), true, true);
        }
        figure.Freeze();
        dc.DrawGeometry(null, HandleHalo, figure);
        dc.DrawGeometry(null, Active(handle) ? HandleActive : HandleLine, figure);
    }

    private void Bar(DrawingContext dc, Point from, Point to, CropHandle handle)
    {
        dc.DrawLine(HandleHalo, from, to);
        dc.DrawLine(Active(handle) ? HandleActive : HandleLine, from, to);
    }

    private bool Active(CropHandle handle) => IsDragging ? drag == handle : hover == handle;

    private void DrawSizeLabel(DrawingContext dc, Rect c)
    {
        var text = new FormattedText($"{crop.Width} × {crop.Height}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double w = text.Width + 16, h = text.Height + 8;
        if (w + 12 > c.Width || h + 12 > c.Height)
            return;
        var box = new Rect(c.Left + (c.Width - w) / 2, c.Top + 10, w, h);
        dc.DrawRoundedRectangle(LabelBack, null, box, 6, 6);
        dc.DrawText(text, new Point(box.Left + 8, box.Top + 4));
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Color color, double thickness, bool round = false)
    {
        var pen = new Pen(Frozen(new SolidColorBrush(color)), thickness);
        if (round)
        { pen.StartLineCap = pen.EndLineCap = PenLineCap.Round; pen.LineJoin = PenLineJoin.Round; }
        pen.Freeze();
        return pen;
    }
}
