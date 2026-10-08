using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZestDrop;

// The ruler moves under a fixed centre marker, rather than jumping to the clicked position.
internal sealed class RotationDial : Slider
{
    internal const double PixelsPerDegree = 5;
    private double lastX, dragAngle;
    private bool dragging;
    private static readonly Brush Face = new SolidColorBrush(Color.FromRgb(250, 251, 252));
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(230, 233, 238)), 1);
    private static readonly Pen Tick = new(new SolidColorBrush(Color.FromRgb(182, 190, 201)), 1);
    private static readonly Pen MajorTick = new(new SolidColorBrush(Color.FromRgb(115, 126, 142)), 1.3);
    internal static string Caption => L.English ? "Rotation angle" : "旋转角度";
    internal static string Help => L.English ? "Drag the ruler · Shift for fine control · Double-click to reset" : "拖动刻度 · 按住 Shift 微调 · 双击归零";

    public RotationDial()
    {
        Minimum = -180; Maximum = 180; SmallChange = 1; LargeChange = 15;
        Height = 80; MinHeight = 80; Focusable = true; ClipToBounds = true; Cursor = Cursors.ScrollWE;
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Slider)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        ToolTip = Help;
        System.Windows.Automation.AutomationProperties.SetName(this, Caption);
    }
    internal static double Signed(double angle)
    {
        angle %= 360;
        return angle > 180 ? angle - 360 : angle < -180 ? angle + 360 : angle;
    }
    internal static double BackendAngle(double angle) => (angle % 360 + 360) % 360;
    internal void BeginDrag() => dragAngle = Value;
    internal void DragBy(double pixels, bool fine)
    {
        // Fixed sensitivity, no acceleration or release inertia. Accumulate substeps for fine dragging.
        dragAngle = Math.Clamp(dragAngle - pixels / PixelsPerDegree * (fine ? .1 : 1), Minimum, Maximum);
        SetCurrentValue(ValueProperty, Math.Round(dragAngle, 1, MidpointRounding.AwayFromZero));
    }
    protected override void OnValueChanged(double oldValue, double newValue)
    { base.OnValueChanged(oldValue, newValue); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, centre = width / 2;
        if (width < 2) return;
        dc.DrawRoundedRectangle(Face, IsKeyboardFocusWithin ? new Pen(UiTheme.Accent, 1) : Outline,
            new Rect(.5, .5, width - 1, ActualHeight - 1), 9, 9);
        dc.PushClip(new RectangleGeometry(new Rect(1, 1, width - 2, ActualHeight - 2), 8, 8));
        var typeface = new Typeface(UiTheme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        for (int degree = -180; degree <= 180; degree++)
        {
            double x = centre + (degree - Value) * PixelsPerDegree;
            if (x < -25 || x > width + 25) continue;
            bool major = degree % 15 == 0;
            dc.DrawLine(major ? MajorTick : Tick, new Point(x, 26), new Point(x, major ? 46 : degree % 5 == 0 ? 39 : 33));
            if (major)
            {
                string text = degree > 0 ? "+" + degree : degree == 0 ? "0" : "−" + Math.Abs(degree);
                var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 11,
                    UiTheme.Muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                if (x - label.Width / 2 >= 24 && x + label.Width / 2 <= width - 24)
                    dc.DrawText(label, new Point(x - label.Width / 2, 52));
            }
        }
        var leftFade = new LinearGradientBrush(Color.FromRgb(250, 251, 252), Color.FromArgb(0, 250, 251, 252), 0);
        var rightFade = new LinearGradientBrush(Color.FromArgb(0, 250, 251, 252), Color.FromRgb(250, 251, 252), 0);
        dc.DrawRectangle(leftFade, null, new Rect(0, 1, 32, ActualHeight - 2));
        dc.DrawRectangle(rightFade, null, new Rect(Math.Max(0, width - 32), 1, 32, ActualHeight - 2));
        dc.DrawRoundedRectangle(UiTheme.Accent, null, new Rect(centre - 1, 12, 2, 31), 1, 1);
        dc.DrawEllipse(UiTheme.Accent, null, new Point(centre, 10), 2.5, 2.5);
        dc.Pop();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus(); e.Handled = true;
        if (e.ClickCount == 2) { dragging = false; ReleaseMouseCapture(); SetCurrentValue(ValueProperty, 0d); return; }
        lastX = e.GetPosition(this).X; BeginDrag(); dragging = CaptureMouse();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging || !IsMouseCaptured) return;
        double x = e.GetPosition(this).X;
        DragBy(x - lastX, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); lastX = x; e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { if (!dragging) return; dragging = false; ReleaseMouseCapture(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { dragging = false; base.OnLostMouseCapture(e); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(Value + e.Delta / 120d * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : 1), 1), Minimum, Maximum));
        e.Handled = true;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : 1;
        double? next = e.Key switch { Key.Left or Key.Down => Value - step, Key.Right or Key.Up => Value + step,
            Key.PageDown => Value - 15, Key.PageUp => Value + 15, Key.Home => 0, _ => null };
        if (next.HasValue) { SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(next.Value, 1), Minimum, Maximum)); e.Handled = true; }
        else base.OnPreviewKeyDown(e);
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
