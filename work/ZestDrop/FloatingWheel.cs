using System;
using System.Collections.Generic;
using System.IO;
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

internal sealed class DropWheel : Window
{
    private readonly Action<string[], Operation> submit;
    private readonly Canvas canvas = new();
    private readonly TextBlock count = new(), hint = new();
    private readonly List<ShapePath> petals = [];
    private readonly List<FrameworkElement> labels = [];
    private readonly List<ShapePath> bevels = [];
    private readonly Border center;
    private static readonly Brush RestingFace=Surface("#FFFFFF","#F1F4F6","#DCE3E8");
    private static readonly Brush SelectedFace=Surface("#FF983F","#C84A13","#A9360B",.34);
    private List<Operation> operations = [];
    private string[] paths = [];
    private bool entered, eligible, tools, allowedCopy;
    private int highlighted = -1;
    public long Instance { get; private set; }
    public bool ToolsMode => tools;
    public bool HasFileDrag => entered && paths.Length > 0;
    private const double Center = 190, Outer = 176, Inner = 57;
    public DropWheel(Action<string[], Operation> submit)
    {
        this.submit = submit;
        Title = L.T("ZestDrop 浮动菜单");
        Icon = AppIcon.Window;
        Width = Height = 380;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        FontFamily = new FontFamily("Segoe UI");
        var shadow=new Ellipse{Width=352,Height=352,Fill=new SolidColorBrush(Color.FromRgb(55,65,75)),Opacity=.28,IsHitTestVisible=false,Effect=new BlurEffect{Radius=9}};
        Canvas.SetLeft(shadow,14);Canvas.SetTop(shadow,20);canvas.Children.Add(shadow);
        var disk = new Ellipse { Width = 360, Height = 360, Fill = Surface("#FFFFFF","#E4E9ED","#B7C2CA"), Stroke = new SolidColorBrush(Color.FromRgb(250,252,253)), StrokeThickness = 1.2,IsHitTestVisible=false };
        Canvas.SetLeft(disk, 10);
        Canvas.SetTop(disk, 10);
        canvas.Children.Add(disk);
        var well=new Ellipse{Width=130,Height=130,Fill=Surface("#C4CFD7","#DFE6EB","#FFFFFF"),Stroke=new SolidColorBrush(Color.FromRgb(239,244,247)),StrokeThickness=1,IsHitTestVisible=false};
        Canvas.SetLeft(well,Center-65);Canvas.SetTop(well,Center-65);Canvas.SetZIndex(well,8);canvas.Children.Add(well);
        center = new Border { Width = 112, Height = 112, CornerRadius = new CornerRadius(56), Background = Surface("#FFFFFF","#FAFCFD","#E9EEF2"),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Effect=new DropShadowEffect{BlurRadius=9,ShadowDepth=3,Opacity=.16,Color=Color.FromRgb(45,61,73)} };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        count.Text = L.T("拖入文件");
        count.TextAlignment = TextAlignment.Center;
        count.FontSize = 15;
        count.FontWeight = FontWeights.SemiBold;
        count.Foreground=UiTheme.Ink;
        hint.Text = L.T("Shift 格式 · Ctrl+Shift 工具");
        hint.TextAlignment = TextAlignment.Center;
        hint.FontSize = 10;
        hint.Foreground = UiTheme.Muted;
        hint.Margin = new Thickness(0, 6, 0, 0);
        text.Children.Add(count);
        text.Children.Add(hint);
        center.Child = text;
        Canvas.SetLeft(center, Center - 56);
        Canvas.SetTop(center, Center - 56);
        Canvas.SetZIndex(center, 10);
        canvas.Children.Add(center);
        Content = canvas;
        PreviewDragEnter += Enter;
        PreviewDragOver += Over;
        PreviewDragLeave += (_, e) => { var p = e.GetPosition(this); if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) Highlight(-1); e.Handled = true; };
        PreviewDrop += OnDrop;
    }
    public void OpenAt(Native.POINT point, bool tools)
    {
        Instance++;
        entered = false;
        eligible = allowedCopy = false;
        paths = [];
        this.tools = tools;
        count.Text = L.T("拖入文件");
        hint.Text = tools ? L.T("工具随类型显示") : L.T("格式随类型显示");
        Render([]);
        if (!IsVisible)
            Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        var area = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        Native.SetWindowPos(hwnd, new IntPtr(-1), point.X, point.Y, 0, 0, 0x0001 | 0x0010);
        int size = (int)Math.Round(380 * Native.GetDpiForWindow(hwnd) / 96.0);
        int x = Math.Clamp(point.X - size / 2, area.Left, Math.Max(area.Left, area.Right - size)), y = Math.Clamp(point.Y - size / 2, area.Top, Math.Max(area.Top, area.Bottom - size));
        Native.SetWindowPos(hwnd, new IntPtr(-1), x, y, size, size, 0x0010 | 0x0040);
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
        Highlight(operations.FindIndex(o => o.Id == highlightId));
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
        operations = paths.Length <= 200 ? Catalog.Options(paths, tools) : [];
        eligible = allowedCopy && operations.Count > 0;
        bool packing = Catalog.PackingOnly(paths);
        count.Text = eligible ? (packing && !tools ? L.T("仅支持打包") : (paths.Length == 1 ? L.T("1 个文件") : L.F("{0} 个文件", paths.Length))) : L.T("无可用操作");
        hint.Text = packing && !tools ? L.T("暂不支持格式转换") : eligible ? L.T("选操作后松手") : L.T("Esc 取消");
        Render(operations);
    }
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
            Journal.Write("DragEntered", new { count = paths.Length, eligible, category = paths.Length > 0 ? Catalog.Category(paths[0]) : "none", allowed = e.AllowedEffects.ToString(), operations = operations.Select(x => x.Id) });
        }
        e.Effects = eligible ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Over(object sender, DragEventArgs e)
    {
        if (!entered)
            Enter(sender, e);
        int index = eligible ? Hit(e.GetPosition(this)) : -1;
        Highlight(index);
        e.Effects = index >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!entered)
            Enter(sender, e);
        int index = eligible ? Hit(e.GetPosition(this)) : -1;
        if (index < 0)
        { e.Effects = DragDropEffects.None; e.Handled = true; Dismiss("InvalidDrop"); return; }
        var selected = operations[index];
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
    private static Brush Surface(string top,string middle,string bottom,double middleOffset=.48)
    {
        var brush=new LinearGradientBrush{StartPoint=new Point(.2,0),EndPoint=new Point(.8,1)};
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(top),0));brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(middle),middleOffset));brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(bottom),1));brush.Freeze();return brush;
    }
    private void Render(List<Operation> actions)
    {
        foreach (var shape in petals)
            canvas.Children.Remove(shape);
        foreach (var label in labels)
            canvas.Children.Remove(label);
        foreach(var bevel in bevels)canvas.Children.Remove(bevel);bevels.Clear();
        petals.Clear();
        labels.Clear();
        highlighted = -1;
        for (int i = 0; i < actions.Count; i++)
        {
            double angle = -Math.PI / 2 + i * 2 * Math.PI / actions.Count, half = Math.PI / actions.Count, start = angle - half + .025, end = angle + half - .025;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(Polar(Inner, start), true, true);
                context.LineTo(Polar(Outer, start), true, false);
                context.ArcTo(Polar(Outer, end), new Size(Outer, Outer), 0, end - start > Math.PI, SweepDirection.Clockwise, true, false);
                context.LineTo(Polar(Inner, end), true, false);
                context.ArcTo(Polar(Inner, start), new Size(Inner, Inner), 0, end - start > Math.PI, SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            var shape = new ShapePath { Data = geometry, Fill = RestingFace, Stroke = new SolidColorBrush(Color.FromRgb(200,210,217)), StrokeThickness = .8,Effect=new DropShadowEffect{BlurRadius=3,ShadowDepth=1.2,Opacity=.11,Color=Color.FromRgb(52,68,80)} };
            petals.Add(shape);
            canvas.Children.Add(shape);
            var bevel=new ShapePath{Data=geometry,Fill=null,Stroke=new SolidColorBrush(Color.FromArgb(150,255,255,255)),StrokeThickness=.6,IsHitTestVisible=false};bevels.Add(bevel);canvas.Children.Add(bevel);
            var label = new TextBlock { Text = actions[i].Label, Foreground=UiTheme.Ink,FontSize = actions.Count > 7 ? 12 : 14, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Width = 80, Height = 44, IsHitTestVisible = false };
            Point p = Polar(119, angle);
            Canvas.SetLeft(label, p.X - 40);
            Canvas.SetTop(label, p.Y - 16);
            labels.Add(label);
            canvas.Children.Add(label);
        }
        UpdateLayout();
    }
    private int Hit(Point point) { if((point-new Point(Center,Center)).Length<=65)return -1;for (int i = 0; i < petals.Count; i++) if (petals[i].Data.FillContains(point)) return i; return -1; }
    private void Highlight(int index)
    {
        if (index == highlighted)
            return;
        highlighted = index;
        for (int i = 0; i < petals.Count; i++)
        {
            bool selected=i==index;petals[i].Fill=selected?SelectedFace:RestingFace;
            petals[i].Stroke=selected?new SolidColorBrush(Color.FromRgb(223,99,24)):new SolidColorBrush(Color.FromRgb(200,210,217));
            petals[i].Effect=new DropShadowEffect{BlurRadius=selected?6:3,ShadowDepth=selected?2.5:1.2,Opacity=selected?.24:.11,Color=selected?Color.FromRgb(173,69,20):Color.FromRgb(52,68,80)};
            labels[i].SetValue(TextBlock.ForegroundProperty,selected?Brushes.White:UiTheme.Ink);
            labels[i].Effect=selected?new DropShadowEffect{BlurRadius=1,ShadowDepth=.7,Opacity=.28,Color=Color.FromRgb(110,38,8)}:null;
            bevels[i].Stroke=selected?new SolidColorBrush(Color.FromArgb(205,255,210,150)):new SolidColorBrush(Color.FromArgb(150,255,255,255));
        }
        hint.Text = index >= 0 ? operations[index].Label : Catalog.PackingOnly(paths) ? L.T("暂不支持格式转换") : L.T("选操作后松手");
    }
}
