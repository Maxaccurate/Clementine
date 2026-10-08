using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using ShapePath = System.Windows.Shapes.Path;

namespace ZestDrop;

// The wheel shown while dragging files. With fewer than ten choices it is one wheel. With more, the first wheel lists
// categories (each shows a preview of the tools inside); hovering one opens a second wheel around the first with that
// category's tools, and the drop happens on one of those.
internal sealed class DropWheel : Window
{
    private sealed class Ring
    {
        public readonly List<ShapePath> Petals = [], Sidewalls = [];
        public readonly List<FrameworkElement> Labels = [];
        public int Highlighted = -1;
    }
    private readonly Action<string[], Operation> submit;
    private readonly Grid stage = new();
    private readonly Canvas canvas = new() { Width = 380, Height = 380 };
    private readonly TextBlock count = new(), hint = new();
    private readonly Ring first = new(), second = new();
    private readonly Border center;
    private static readonly Brush RestingFace = Solid("#F1F3F2"), SelectedFace = Solid("#FF6208"), OpenFace = Solid("#FFE2CF");
    private static readonly Brush RestingSide = Solid("#DDE2DE"), SelectedSide = Solid("#D94D04"), OpenSide = Solid("#F0C3A5");
    private static readonly Brush RestingOutline = Solid("#CFD5D1"), OpenOutline = Solid("#F2B791");
    private static readonly Brush OuterFace = Solid("#F7F8F7");
    private List<Operation> operations = [];
    private List<ToolGroup>? groups;
    private int openGroup = -1;
    private string[] paths = [];
    private bool entered, eligible, tools, allowedCopy;
    public long Instance { get; private set; }
    public bool ToolsMode => tools;
    public bool HasFileDrag => entered && paths.Length > 0;
    // The size of the window's picture: 380 for one wheel, larger when a second wheel can open around it.
    public double StageSize { get; private set; } = SingleSize;
    private const double SingleSize = 380, DoubleSize = 640;
    private const double Center = 190, Outer = 172, Inner = 65, OuterRingInner = 184, OuterRingOuter = 300;

    public DropWheel(Action<string[], Operation> submit)
    {
        this.submit = submit;
        Title = L.T("ZestDrop 浮动菜单");
        Icon = AppIcon.Window;
        Width = Height = SingleSize;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        FontFamily = UiTheme.Font;
        var disk = new Ellipse { Width = 360, Height = 360, Fill = Solid("#EAEEEB"), IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 5, Opacity = .13, Color = Color.FromRgb(42, 50, 45) } };
        Canvas.SetLeft(disk, 10);
        Canvas.SetTop(disk, 10);
        canvas.Children.Add(disk);
        var hubSide = new Ellipse { Width = 112, Height = 112, Fill = RestingSide, IsHitTestVisible = false,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = .08, Color = Color.FromRgb(42, 50, 45) } };
        Canvas.SetLeft(hubSide, Center - 56); Canvas.SetTop(hubSide, Center - 56 + 4);
        Canvas.SetZIndex(hubSide, 9); canvas.Children.Add(hubSide);
        center = new Border { Width = 112, Height = 112, CornerRadius = new CornerRadius(56), Background = Brushes.White,
            BorderBrush = Solid("#F8FAF8"), BorderThickness = new Thickness(1) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        count.Text = L.T("拖入文件");
        count.TextAlignment = TextAlignment.Center;
        count.FontSize = 15;
        count.FontWeight = FontWeights.Medium;
        count.Foreground = UiTheme.Ink;
        hint.Text = L.T("Shift 格式 · Ctrl+Shift 工具");
        hint.TextAlignment = TextAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;
        hint.FontSize = 10;
        hint.Foreground = UiTheme.Muted;
        hint.Margin = new Thickness(6, 6, 6, 0);
        text.Children.Add(count);
        text.Children.Add(hint);
        center.Child = text;
        Canvas.SetLeft(center, Center - 56);
        Canvas.SetTop(center, Center - 56);
        Canvas.SetZIndex(center, 10);
        canvas.Children.Add(center);
        stage.Children.Add(canvas);
        Content = stage;
        PreviewDragEnter += Enter;
        PreviewDragOver += Over;
        PreviewDragLeave += (_, e) => { var p = e.GetPosition(this); if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) { Highlight(first, -1); Highlight(second, -1); } e.Handled = true; };
        PreviewDrop += OnDrop;
    }

    public void OpenAt(Native.POINT point, bool tools)
    {
        Instance++;
        entered = false;
        eligible = allowedCopy = false;
        paths = [];
        groups = null;
        this.tools = tools;
        count.Text = L.T("拖入文件");
        hint.Text = tools ? L.T("工具随类型显示") : L.T("格式随类型显示");
        Render([]);
        Resize(SingleSize);
        if (!IsVisible)
            Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        var area = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        Native.SetWindowPos(hwnd, new IntPtr(-1), point.X, point.Y, 0, 0, 0x0001 | 0x0010);
        int size = (int)Math.Round(SingleSize * Native.GetDpiForWindow(hwnd) / 96.0);
        int x = Math.Clamp(point.X - size / 2, area.Left, Math.Max(area.Left, area.Right - size)), y = Math.Clamp(point.Y - size / 2, area.Top, Math.Max(area.Top, area.Bottom - size));
        Native.SetWindowPos(hwnd, new IntPtr(-1), x, y, size, size, 0x0010 | 0x0040);
        UpdateLayout();
    }

    // Grows or shrinks the window around the wheel's centre so a second wheel has room.
    private void Resize(double size)
    {
        if (Math.Abs(size - StageSize) < .5)
            return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !IsVisible)
        { StageSize = size; Width = Height = size; return; }
        Native.GetWindowRect(hwnd, out var current);
        double scale = Native.GetDpiForWindow(hwnd) / 96.0;
        int pixels = (int)Math.Round(size * scale), midX = (current.Left + current.Right) / 2, midY = (current.Top + current.Bottom) / 2;
        var area = Forms.Screen.FromPoint(new System.Drawing.Point(midX, midY)).WorkingArea;
        int x = Math.Clamp(midX - pixels / 2, area.Left, Math.Max(area.Left, area.Right - pixels)), y = Math.Clamp(midY - pixels / 2, area.Top, Math.Max(area.Top, area.Bottom - pixels));
        StageSize = size;
        Native.SetWindowPos(hwnd, new IntPtr(-1), x, y, pixels, pixels, 0x0010 | 0x0040);
        UpdateLayout();
    }

    public void Dismiss(string reason)
    {
        if (!IsVisible)
            return;
        Hide();
        paths = [];
        eligible = false;
        entered = false;
        Journal.Write("WheelDismissed", new { reason });
    }

    // For screenshots: show the wheel as if these files were being dragged over it, with one option lit.
    public void Preview(string[] files, bool toolsMode, string highlightId)
    {
        tools = toolsMode;
        paths = files;
        entered = allowedCopy = true;
        Populate();
        if (groups != null)
        {
            int group = groups.FindIndex(g => g.Items.Any(o => o.Id == highlightId));
            if (group >= 0)
            {
                OpenGroup(group);
                Highlight(second, groups[group].Items.FindIndex(o => o.Id == highlightId));
            }
        }
        else
            Highlight(first, operations.FindIndex(o => o.Id == highlightId));
    }

    public void ChangeMode(bool nextTools)
    {
        tools = nextTools;
        if (!entered)
        { hint.Text = tools ? L.T("工具随类型显示") : L.T("格式随类型显示"); return; }
        Populate();
    }

    private void Populate()
    {
        var all = paths.Length <= 200 ? Catalog.Options(paths, tools) : [];
        groups = tools && paths.Length > 0 ? Catalog.Grouped(all, Catalog.Category(paths[0])) : null;
        openGroup = -1;
        RenderSecond(null);
        operations = groups == null ? all : groups.Select((g, i) => new Operation("__group:" + i, g.Label)).ToList();
        eligible = allowedCopy && all.Count > 0;
        bool packing = Catalog.PackingOnly(paths);
        count.Text = eligible ? (packing && !tools ? L.T("仅支持打包") : (paths.Length == 1 ? L.T("1 个文件") : L.F("{0} 个文件", paths.Length))) : L.T("无可用操作");
        hint.Text = packing && !tools ? L.T("暂不支持格式转换") : eligible ? Prompt() : L.T("Esc 取消");
        Render(operations);
        Resize(groups != null ? DoubleSize : SingleSize);
    }

    private string Prompt() => groups != null ? L.T("移到分类上，再选工具后松手") : L.T("选操作后松手");

    private void Enter(object sender, DragEventArgs e)
    {
        if (!entered)
        {
            entered = true;
            try
            { paths = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] ?? [] : []; }
            catch { paths = []; }
            allowedCopy = (e.AllowedEffects & DragDropEffects.Copy) != 0;
            Populate();
            Journal.Write("DragEntered", new { count = paths.Length, eligible, category = paths.Length > 0 ? Catalog.Category(paths[0]) : "none", allowed = e.AllowedEffects.ToString(), operations = (groups?.SelectMany(g => g.Items) ?? operations).Select(x => x.Id) });
        }
        e.Effects = eligible ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Over(object sender, DragEventArgs e)
    {
        if (!entered)
            Enter(sender, e);
        bool actionable = false;
        if (eligible)
        {
            var point = e.GetPosition(canvas);
            int tool = openGroup >= 0 ? Hit(second, point, OuterRingInner, OuterRingOuter + 10) : -1;
            int category = tool >= 0 ? -1 : Hit(first, point, Inner, Outer);
            if (tool >= 0)
            { Highlight(second, tool); actionable = true; }
            else if (groups != null && category >= 0)
            { OpenGroup(category); Highlight(second, -1); actionable = true; }
            else if (groups == null)
            { Highlight(first, category); actionable = category >= 0; }
            else
                Highlight(second, -1);
        }
        e.Effects = actionable ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!entered)
            Enter(sender, e);
        Operation? selected = null;
        if (eligible)
        {
            var point = e.GetPosition(canvas);
            if (openGroup >= 0 && Hit(second, point, OuterRingInner, OuterRingOuter + 10) is var tool and >= 0)
                selected = groups![openGroup].Items[tool];
            else if (groups == null && Hit(first, point, Inner, Outer) is var index and >= 0)
                selected = operations[index];
        }
        if (selected == null)
        { e.Effects = DragDropEffects.None; e.Handled = true; Dismiss("InvalidDrop"); return; }
        var files = paths.ToArray();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        Hide();
        paths = [];
        entered = false;
        eligible = false;
        Journal.Write("DropAccepted", new { Action = selected.Id, count = files.Length, effect = "Copy" });
        Dispatcher.InvokeAsync(() => submit(files, selected));
    }

    private static Point Polar(double radius, double angle) => new(Center + radius * Math.Cos(angle), Center + radius * Math.Sin(angle));

    private static Brush Solid(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Geometry Keycap(double start, double end, double inner = Inner, double outer = Outer)
    {
        // Flat circular faces with softly rounded junctions between the arcs and radial edges.
        const double outerCorner = 16, innerCorner = 10;
        double outerInset = outerCorner / outer, innerInset = innerCorner / inner;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(inner + innerCorner, start), true, true);
            context.LineTo(Polar(outer - outerCorner, start), true, false);
            context.QuadraticBezierTo(Polar(outer, start), Polar(outer, start + outerInset), true, false);
            context.ArcTo(Polar(outer, end - outerInset), new Size(outer, outer), 0,
                end - start - 2 * outerInset > Math.PI, SweepDirection.Clockwise, true, false);
            context.QuadraticBezierTo(Polar(outer, end), Polar(outer - outerCorner, end), true, false);
            context.LineTo(Polar(inner + innerCorner, end), true, false);
            context.QuadraticBezierTo(Polar(inner, end), Polar(inner, end - innerInset), true, false);
            context.ArcTo(Polar(inner, start + innerInset), new Size(inner, inner), 0,
                end - start - 2 * innerInset > Math.PI, SweepDirection.Counterclockwise, true, false);
            context.QuadraticBezierTo(Polar(inner, start), Polar(inner + innerCorner, start), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void Clear(Ring ring)
    {
        foreach (var shape in ring.Petals)
            canvas.Children.Remove(shape);
        foreach (var label in ring.Labels)
            canvas.Children.Remove(label);
        foreach (var sidewall in ring.Sidewalls)
            canvas.Children.Remove(sidewall);
        ring.Petals.Clear();
        ring.Labels.Clear();
        ring.Sidewalls.Clear();
        ring.Highlighted = -1;
    }

    // One petal with its label. The label is a title and, for categories, a small preview line of the tools inside.
    private void AddPetal(Ring ring, double start, double end, double inner, double outer, double labelRadius, double labelWidth, string title, string? preview, double titleSize, Brush face, int layer)
    {
        var geometry = Keycap(start, end, inner, outer);
        var sidewall = new ShapePath { Data = geometry, Fill = RestingSide, IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(0, 4),
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Opacity = .09, Color = Color.FromRgb(42, 50, 45) } };
        Canvas.SetZIndex(sidewall, layer); ring.Sidewalls.Add(sidewall); canvas.Children.Add(sidewall);
        var shape = new ShapePath { Data = geometry, Fill = face, Stroke = RestingOutline, StrokeThickness = .7 };
        Canvas.SetZIndex(shape, layer + 1);
        ring.Petals.Add(shape);
        canvas.Children.Add(shape);
        var label = new StackPanel { Width = labelWidth, IsHitTestVisible = false };
        label.Children.Add(new TextBlock { Text = title, FontFamily = UiTheme.Font, Foreground = UiTheme.Ink, FontSize = titleSize, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (preview != null)
            label.Children.Add(new TextBlock { Text = preview, FontFamily = UiTheme.Font, Foreground = UiTheme.Muted, FontSize = 9, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 26, Margin = new Thickness(0, 3, 0, 0) });
        label.Measure(new Size(labelWidth, double.PositiveInfinity));
        Point p = Polar(labelRadius, (start + end) / 2);
        Canvas.SetLeft(label, p.X - labelWidth / 2);
        Canvas.SetTop(label, p.Y - label.DesiredSize.Height / 2);
        Canvas.SetZIndex(label, layer + 2);
        ring.Labels.Add(label);
        canvas.Children.Add(label);
    }

    private void Render(List<Operation> actions)
    {
        Clear(first);
        for (int i = 0; i < actions.Count; i++)
        {
            double angle = -Math.PI / 2 + i * 2 * Math.PI / actions.Count, half = Math.PI / actions.Count;
            double labelWidth = Math.Clamp(2 * 119 * Math.Sin(half) - 16, 72, groups != null ? 92 : 112);
            string? preview = groups != null ? Preview(groups[i]) : null;
            AddPetal(first, angle - half + .016, angle + half - .016, Inner, Outer, 119, labelWidth, actions[i].Label, preview, groups != null ? 13 : actions.Count > 7 ? 12 : 14, RestingFace, 1);
        }
        UpdateLayout();
    }

    private static string Preview(ToolGroup group) => string.Join(" · ", group.Items.Take(2).Select(o => o.Label)) + (group.Items.Count > 2 ? " …" : "");

    // The second wheel: the tools of one category, fanned out around the first wheel on the side of that category.
    private void OpenGroup(int index)
    {
        if (groups == null || index == openGroup)
            return;
        openGroup = index;
        RenderSecond(groups[index]);
        for (int i = 0; i < first.Petals.Count; i++)
            Paint(first, i, i == index ? OpenFace : RestingFace, i == index ? OpenSide : RestingSide, i == index ? OpenOutline : RestingOutline, UiTheme.Ink);
        hint.Text = L.T("选一个工具后松手");
    }

    private void RenderSecond(ToolGroup? group)
    {
        Clear(second);
        if (group == null || groups == null)
            return;
        int groupIndex = groups.IndexOf(group), n = group.Items.Count;
        double direction = -Math.PI / 2 + groupIndex * 2 * Math.PI / groups.Count;
        double width = Math.Min(Math.PI * 2 * .92 / n, 32 * Math.PI / 180), total = width * n;
        for (int i = 0; i < n; i++)
        {
            double middle = direction - total / 2 + width * (i + .5), half = width / 2;
            double labelWidth = Math.Clamp(2 * 242 * Math.Sin(half) - 16, 70, 104);
            AddPetal(second, middle - half + .018, middle + half - .018, OuterRingInner, OuterRingOuter, 242, labelWidth, group.Items[i].Label, null, 12.5, OuterFace, 20);
        }
        UpdateLayout();
    }

    private static int Hit(Ring ring, Point point, double inner, double outer)
    {
        double distance = (point - new Point(Center, Center)).Length;
        if (distance <= inner || distance > outer)
            return -1;
        for (int i = 0; i < ring.Petals.Count; i++)
            if (ring.Petals[i].Data.FillContains(point))
                return i;
        return -1;
    }

    private static void Paint(Ring ring, int index, Brush face, Brush side, Brush outline, Brush text)
    {
        ring.Petals[index].Fill = face;
        ring.Petals[index].Stroke = outline;
        ring.Sidewalls[index].Fill = side;
        foreach (var block in ((StackPanel)ring.Labels[index]).Children.OfType<TextBlock>().Take(1))
            block.Foreground = text;
        if (text == Brushes.White)
            foreach (var block in ((StackPanel)ring.Labels[index]).Children.OfType<TextBlock>().Skip(1))
                block.Foreground = Brushes.White;
        else
            foreach (var block in ((StackPanel)ring.Labels[index]).Children.OfType<TextBlock>().Skip(1))
                block.Foreground = UiTheme.Muted;
    }

    private void Highlight(Ring ring, int index)
    {
        if (index == ring.Highlighted)
            return;
        ring.Highlighted = index;
        for (int i = 0; i < ring.Petals.Count; i++)
        {
            bool selected = i == index;
            bool openCategory = ring == first && groups != null && i == openGroup;
            Brush face = selected ? SelectedFace : openCategory ? OpenFace : ring == second ? OuterFace : RestingFace;
            Paint(ring, i, face, selected ? SelectedSide : openCategory ? OpenSide : RestingSide, selected ? SelectedFace : openCategory ? OpenOutline : RestingOutline, selected ? Brushes.White : UiTheme.Ink);
        }
        if (ring == second && index >= 0 && openGroup >= 0)
            hint.Text = groups![openGroup].Items[index].Label;
        else if (ring == first)
            hint.Text = index >= 0 ? operations[index].Label : Catalog.PackingOnly(paths) ? L.T("暂不支持格式转换") : Prompt();
        else if (openGroup >= 0)
            hint.Text = groups![openGroup].Label;
    }
}
