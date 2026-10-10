using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZestDrop;

// A continuous-curvature approximation: curvature starts and ends at zero where the corner joins a straight edge.
internal sealed class ContinuousFrame : Decorator
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(ContinuousFrame),
        new FrameworkPropertyMetadata(20d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange));
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
    // The window's own colours, taken when it opens.
    private readonly Brush face = UiTheme.WindowBack;
    private readonly Pen edge = new(UiTheme.Line, 1);

    internal static Geometry Outline(Rect bounds, double radius)
    {
        double span = Math.Clamp(radius * 1.2, 0, Math.Min(bounds.Width, bounds.Height) / 2);
        if (span == 0) return new RectangleGeometry(bounds);
        double left = bounds.Left, top = bounds.Top, right = bounds.Right, bottom = bounds.Bottom;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(left + span, top), true, true);
            path.LineTo(new Point(right - span, top), true, false);
            path.BezierTo(new Point(right - .67 * span, top), new Point(right - .4 * span, top), new Point(right - .2 * span, top + .2 * span), true, false);
            path.BezierTo(new Point(right, top + .4 * span), new Point(right, top + .67 * span), new Point(right, top + span), true, false);
            path.LineTo(new Point(right, bottom - span), true, false);
            path.BezierTo(new Point(right, bottom - .67 * span), new Point(right, bottom - .4 * span), new Point(right - .2 * span, bottom - .2 * span), true, false);
            path.BezierTo(new Point(right - .4 * span, bottom), new Point(right - .67 * span, bottom), new Point(right - span, bottom), true, false);
            path.LineTo(new Point(left + span, bottom), true, false);
            path.BezierTo(new Point(left + .67 * span, bottom), new Point(left + .4 * span, bottom), new Point(left + .2 * span, bottom - .2 * span), true, false);
            path.BezierTo(new Point(left, bottom - .4 * span), new Point(left, bottom - .67 * span), new Point(left, bottom - span), true, false);
            path.LineTo(new Point(left, top + span), true, false);
            path.BezierTo(new Point(left, top + .67 * span), new Point(left, top + .4 * span), new Point(left + .2 * span, top + .2 * span), true, false);
            path.BezierTo(new Point(left + .4 * span, top), new Point(left + .67 * span, top), new Point(left + span, top), true, false);
        }
        geometry.Freeze(); return geometry;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null) return new Size(2, 2);
        Child.Measure(new Size(Math.Max(0, availableSize.Width - 2), Math.Max(0, availableSize.Height - 2)));
        return new Size(Child.DesiredSize.Width + 2, Child.DesiredSize.Height + 2);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var childSize = new Size(Math.Max(0, finalSize.Width - 2), Math.Max(0, finalSize.Height - 2));
        if (Child != null)
        {
            Child.Arrange(new Rect(new Point(1, 1), childSize));
            Child.Clip = Outline(new Rect(childSize), Math.Max(0, CornerRadius - 1));
        }
        return finalSize;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 1 || ActualHeight < 1) return;
        dc.DrawGeometry(face, edge, Outline(new Rect(.5, .5, ActualWidth - 1, ActualHeight - 1), CornerRadius));
    }
}
