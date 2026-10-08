using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using ZestDrop;

internal static class Checks
{
    [STAThread] public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        L.UseForSession("zh");
        string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("outputs/ui-controls");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        int failed = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { test = name, passed = true }); }
            catch (Exception ex) { failed++; results.Add(new { test = name, passed = false, error = ex.GetBaseException().Message }); }
        }
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        StartupChecks.Run(Check);
        var window = new Window { Width = 400, Height = 445, ShowActivated = false, ShowInTaskbar = false, Left = -5000, Top = -5000 };
        UiTheme.Apply(window);
        var panel = new StackPanel();
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 13, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "处理参数", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        Label("旋转角度（0–360°，顺时针）");
        var angle = new TextBox { Text = "90" }; panel.Children.Add(angle);
        var slider = new Slider { Minimum = 0, Maximum = 360, Value = 90, SmallChange = 1, LargeChange = 15, Margin = new Thickness(0, 6, 0, 0) };
        slider.ValueChanged += (_, _) => angle.Text = Math.Round(slider.Value).ToString(); panel.Children.Add(slider);
        Label("翻转");
        var combo = new ComboBox();
        foreach (var item in new[] { ("不翻转", "none"), ("水平翻转", "horizontal"), ("垂直翻转", "vertical") })
            combo.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        combo.SelectedIndex = 0; panel.Children.Add(combo);
        Label("非直角旋转时的画布");
        var canvasChoice = new ComboBox();
        canvasChoice.Items.Add("放大画布，保留整张画面"); canvasChoice.Items.Add("保留原始画布大小"); canvasChoice.SelectedIndex = 0; panel.Children.Add(canvasChoice);
        Label("空白处颜色（留空为透明）"); panel.Children.Add(new TextBox());
        var root = UiTheme.Card(panel, new Thickness(20)); root.Margin = new Thickness(12); window.Content = root;
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.Measure(new Size(400, 445)); root.Arrange(new Rect(0, 0, 400, 445)); root.UpdateLayout();
        void Pump() => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Check("window corners use smooth alpha edges instead of a binary clipping region", () =>
        {
            IntPtr region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                int type = GetWindowRgn(new WindowInteropHelper(window).Handle, region);
                Require(window.AllowsTransparency && (type == 0 || PtInRegion(region, 0, 0)), "A hard native corner region would cut off the antialiased edge.");
                var frame = (FrameworkElement)UiTheme.Frame(window, new Grid());
                frame.Measure(new Size(400, 200)); frame.Arrange(new Rect(0, 0, 400, 200)); frame.UpdateLayout();
                var bitmap = new RenderTargetBitmap(400, 200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var pixels = new byte[400 * 200 * 4]; bitmap.CopyPixels(pixels, 400 * 4, 0);
                int partial = 0;
                for (int y = 0; y < 26; y++) for (int x = 0; x < 26; x++)
                { int alpha = pixels[(y * 400 + x) * 4 + 3]; if (alpha > 0 && alpha < 255) partial++; }
                Require(pixels[3] == 0 && partial > 10, "The corner must contain transparent pixels and blended edge pixels.");
                Save(frame, Path.Combine(output, "smooth-corner.png"));
                var reference = new Border { CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)), BorderBrush = new SolidColorBrush(Color.FromRgb(230, 233, 238)), BorderThickness = new Thickness(1) };
                reference.Measure(new Size(400, 200)); reference.Arrange(new Rect(0, 0, 400, 200)); reference.UpdateLayout();
                Save(reference, Path.Combine(output, "circular-corner.png"));
            }
            finally { DeleteObject(region); }
        });
        Check("continuous frame preserves more corner area and squares off when maximized", () =>
        {
            var bounds = new Rect(0, 0, 400, 200);
            Require(ContinuousFrame.Outline(bounds, 20).FillContains(new Point(5, 5)), "Continuous corner should preserve the restrained, squarer silhouette.");
            Require(!new RectangleGeometry(bounds, 20, 20).FillContains(new Point(5, 5)), "Reference circular corner unexpectedly changed.");
            Require(ContinuousFrame.Outline(bounds, 0).FillContains(new Point(.5, .5)), "Maximized frame must retain its square edge.");
        });
        Check("slider dragging updates the value and parameter subscriber", () =>
        {
            var track = (Track)slider.Template.FindName("PART_Track", slider);
            double before = slider.Value;
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(30, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Require(slider.Value > before && angle.Text == Math.Round(slider.Value).ToString(), "Drag or value binding did not update the parameter.");
        });
        Check("slider keyboard commands and bounds remain functional", () =>
        {
            slider.Value = 90; Slider.IncreaseSmall.Execute(null, slider);
            Require(slider.Value == 91, "Small increment command failed.");
            Slider.DecreaseLarge.Execute(null, slider); Require(slider.Value == 76, "Large decrement command failed.");
            slider.Value = 500; Require(slider.Value == 360, "Upper bound failed.");
            slider.Value = -20; Require(slider.Value == 0, "Lower bound failed.");
        });
        Check("dropdown toggle opens and closes the native selection popup", () =>
        {
            var toggle = (ToggleButton)combo.Template.FindName("toggle", combo);
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            toggle.IsChecked = true; Pump(); Require(combo.IsDropDownOpen && popup.IsOpen, "Open binding failed.");
            popup.Child.Measure(new Size(400, 500)); popup.Child.Arrange(new Rect(popup.Child.DesiredSize)); popup.Child.UpdateLayout();
            Save(popup.Child, Path.Combine(output, "dropdown.png"));
            toggle.IsChecked = false; Pump(); Require(!combo.IsDropDownOpen && !popup.IsOpen, "Close binding failed.");
        });
        Check("selection retains operation values and notifies the editor", () =>
        {
            int changed = 0; combo.SelectionChanged += (_, _) => changed++;
            combo.SelectedIndex = 1; Pump();
            Require(changed == 1 && ((ComboBoxItem)combo.SelectedItem).Tag?.ToString() == "horizontal", "Selection or operation value was lost.");
        });
        Check("standard accessibility providers remain available", () =>
        {
            var sliderPeer = new SliderAutomationPeer(slider);
            var range = (IRangeValueProvider)sliderPeer.GetPattern(PatternInterface.RangeValue)!;
            range.SetValue(45); Require(slider.Value == 45, "Range provider failed.");
            var comboPeer = new ComboBoxAutomationPeer(combo);
            var expand = (IExpandCollapseProvider)comboPeer.GetPattern(PatternInterface.ExpandCollapse)!;
            expand.Expand(); Pump(); Require(combo.IsDropDownOpen, "Accessible expand failed.");
            expand.Collapse(); Pump(); Require(!combo.IsDropDownOpen, "Accessible collapse failed.");
        });
        Check("editable selections keep their native text input", () =>
        {
            canvasChoice.IsEditable = true; canvasChoice.ApplyTemplate(); Pump();
            var editor = (TextBox)canvasChoice.Template.FindName("PART_EditableTextBox", canvasChoice);
            Require(editor.Visibility == Visibility.Visible, "Editable input was hidden.");
            editor.Text = "自定义"; Pump(); Require(canvasChoice.Text == "自定义", "Text binding failed.");
            canvasChoice.IsEditable = false; canvasChoice.SelectedIndex = 0;
        });
        slider.Value = 90; combo.SelectedIndex = 0; root.UpdateLayout(); Save(root, Path.Combine(output, "controls.png"));
        combo.IsEnabled = false; slider.IsEnabled = false; root.UpdateLayout(); Save(root, Path.Combine(output, "disabled.png"));
        var dial = new RotationDial();
        var dialPanel = new StackPanel { Margin = new Thickness(16), Width = 598 };
        dialPanel.Children.Add(new TextBlock { Text = RotationDial.Caption, Margin = new Thickness(0, 0, 0, 12), FontFamily = UiTheme.Font, FontSize = 14 });
        dialPanel.Children.Add(dial);
        dialPanel.Measure(new Size(630, 150)); dialPanel.Arrange(new Rect(0, 0, 630, 150)); dialPanel.UpdateLayout();
        Check("rotation dial has stable drag sensitivity and fine control", () =>
        {
            dial.Value = 0; dial.BeginDrag(); dial.DragBy(-100, false); Require(dial.Value == 20, "Normal drag sensitivity changed.");
            dial.Value = 0; dial.BeginDrag(); dial.DragBy(-100, true); Require(dial.Value == 2, "Fine control must be ten times slower.");
            dial.Value = 0; dial.BeginDrag(); for (int i = 0; i < 50; i++) dial.DragBy(-1, true);
            Require(dial.Value == 1, "Small fine-drag events must accumulate instead of being discarded.");
        });
        Check("rotation stops at either limit and immediately reverses direction", () =>
        {
            dial.Value = 170; dial.BeginDrag(); dial.DragBy(-500, false); Require(dial.Value == 180, "Positive limit failed.");
            dial.DragBy(5, false); Require(dial.Value == 179, "Overshoot must not create a dead zone on reversing.");
            dial.Value = -170; dial.BeginDrag(); dial.DragBy(500, false); Require(dial.Value == -180, "Negative limit failed.");
            dial.DragBy(-5, false); Require(dial.Value == -179, "Negative limit reversal failed.");
        });
        Check("signed quarter turns and export angles preserve image and video worker contracts", () =>
        {
            Require(RotationDial.Signed(270) == -90 && RotationDial.Signed(-270) == 90, "Quarter turn normalization failed.");
            Require(RotationDial.BackendAngle(-15) == 345 && RotationDial.BackendAngle(-180) == 180, "Negative angles must export as equivalent clockwise angles.");
            Require(RotationDial.BackendAngle(15) == 15 && RotationDial.BackendAngle(0) == 0, "Positive angles changed.");
        });
        Check("rotation dial exposes the signed range to accessibility tools", () =>
        {
            var range = (IRangeValueProvider)new SliderAutomationPeer(dial).GetPattern(PatternInterface.RangeValue)!;
            Require(range.Minimum == -180 && range.Maximum == 180, "Incorrect accessible range.");
            range.SetValue(-45); Require(dial.Value == -45, "Accessible negative value failed.");
        });
        Check("rotation crop dimensions match the export geometry for signed angles", () =>
        {
            Require(RotationGeometry.CropSize(401, 401, 28) == (293, 293), "Square photo crop dimensions changed.");
            Require(RotationGeometry.CropSize(160, 120, 45) == (93, 70), "Landscape crop dimensions changed.");
            Require(RotationGeometry.CropSize(160, 120, 30) == RotationGeometry.CropSize(160, 120, -30), "Signed preview and export crop dimensions disagree.");
        });
        foreach (double angleValue in new[] { 0d, 30d, -30d, 180d })
        { dial.Value = angleValue; dialPanel.UpdateLayout(); Save(dialPanel, Path.Combine(output, "dial-" + angleValue + ".png")); }
        window.Close();
        File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { passed = results.Count - failed, failed, checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"passed={results.Count - failed}, failed={failed}");
        return failed == 0 ? 0 : 1;
    }
    private static void Save(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.RenderSize.Width)), Math.Max(1, (int)Math.Ceiling(element.RenderSize.Height)), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
}
