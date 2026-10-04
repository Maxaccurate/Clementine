using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms=System.Windows.Forms;
using ShapePath=System.Windows.Shapes.Path;

namespace ZestDrop;

internal sealed class DropWheel:Window
{
    private readonly Action<string[],Operation> submit;
    private readonly Canvas canvas=new();
    private readonly TextBlock count=new(),hint=new();
    private readonly List<ShapePath> petals=[];
    private readonly List<FrameworkElement> labels=[];
    private List<Operation> operations=[];
    private string[] paths=[];
    private bool entered,eligible,tools;
    private int highlighted=-1;
    public long Instance { get; private set; }
    private const double Center=190,Outer=176,Inner=57;
    public DropWheel(Action<string[],Operation> submit)
    {
        this.submit=submit;Title="ZestDrop 浮动菜单";Icon=AppIcon.Window;Width=Height=380;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;
        Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;AllowDrop=true;FontFamily=new FontFamily("Segoe UI");
        var disk=new Ellipse{Width=368,Height=368,Fill=new SolidColorBrush(Color.FromArgb(250,248,249,250)),Stroke=new SolidColorBrush(Color.FromRgb(226,228,232)),StrokeThickness=1};
        Canvas.SetLeft(disk,6);Canvas.SetTop(disk,6);canvas.Children.Add(disk);
        var center=new Border{Width=112,Height=104,CornerRadius=new CornerRadius(50),Background=Brushes.White};
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        count.Text="拖入文件";count.TextAlignment=TextAlignment.Center;count.FontSize=15;count.FontWeight=FontWeights.SemiBold;
        hint.Text="F8 格式 · F9 工具";hint.TextAlignment=TextAlignment.Center;hint.FontSize=10;hint.Foreground=Brushes.DimGray;hint.Margin=new Thickness(0,6,0,0);
        text.Children.Add(count);text.Children.Add(hint);center.Child=text;
        Canvas.SetLeft(center,Center-56);Canvas.SetTop(center,Center-52);Canvas.SetZIndex(center,10);canvas.Children.Add(center);Content=canvas;
        PreviewDragEnter+=Enter;PreviewDragOver+=Over;
        PreviewDragLeave+=(_,e)=>{var p=e.GetPosition(this);if(p.X<0||p.Y<0||p.X>ActualWidth||p.Y>ActualHeight)Highlight(-1);e.Handled=true;};
        PreviewDrop+=OnDrop;
    }
    public void OpenAt(Native.POINT point,bool tools)
    {
        Instance++;entered=false;eligible=false;paths=[];this.tools=tools;
        count.Text="拖入文件";hint.Text=tools?"工具随类型显示":"格式随类型显示";Render([]);
        if(!IsVisible)Show();
        var hwnd=new WindowInteropHelper(this).Handle;var area=Forms.Screen.FromPoint(new System.Drawing.Point(point.X,point.Y)).WorkingArea;
        Native.SetWindowPos(hwnd,new IntPtr(-1),point.X,point.Y,0,0,0x0001|0x0010);
        int size=(int)Math.Round(380*Native.GetDpiForWindow(hwnd)/96.0);
        int x=Math.Clamp(point.X-size/2,area.Left,Math.Max(area.Left,area.Right-size)),y=Math.Clamp(point.Y-size/2,area.Top,Math.Max(area.Top,area.Bottom-size));
        Native.SetWindowPos(hwnd,new IntPtr(-1),x,y,size,size,0x0010|0x0040);UpdateLayout();
    }
    public void Dismiss(string reason)
    {
        if(!IsVisible)return;Hide();paths=[];eligible=false;entered=false;Journal.Write("WheelDismissed",new{reason});
    }
    private void Enter(object sender,DragEventArgs e)
    {
        if(!entered)
        {
            entered=true;try{paths=e.Data.GetDataPresent(DataFormats.FileDrop)?e.Data.GetData(DataFormats.FileDrop)as string[]??[]:[];}catch{paths=[];}
            operations=paths.Length<=200?Catalog.Options(paths,tools):[];eligible=(e.AllowedEffects&DragDropEffects.Copy)!=0&&operations.Count>0;
            bool packing=Catalog.PackingOnly(paths);
            count.Text=packing?"仅支持打包":eligible?$"{paths.Length} 个文件":"无可用操作";
            hint.Text=packing&&!tools?"暂不支持格式转换":eligible?"选操作后松手":"Esc 取消";Render(operations);
            Journal.Write("DragEntered",new{count=paths.Length,eligible,category=paths.Length>0?Catalog.Category(paths[0]):"none",allowed=e.AllowedEffects.ToString(),operations=operations.Select(x=>x.Id)});
        }
        e.Effects=eligible?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
    }
    private void Over(object sender,DragEventArgs e)
    {
        if(!entered)Enter(sender,e);int index=eligible?Hit(e.GetPosition(this)):-1;Highlight(index);e.Effects=index>=0?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;
    }
    private void OnDrop(object sender,DragEventArgs e)
    {
        if(!entered)Enter(sender,e);int index=eligible?Hit(e.GetPosition(this)):-1;
        if(index<0){e.Effects=DragDropEffects.None;e.Handled=true;Dismiss("InvalidDrop");return;}
        var selected=operations[index];var files=paths.ToArray();e.Effects=DragDropEffects.Copy;e.Handled=true;Hide();paths=[];entered=false;eligible=false;
        Journal.Write("DropAccepted",new{Action=selected.Id,count=files.Length,effect="Copy"});Dispatcher.InvokeAsync(()=>submit(files,selected));
    }
    private static Point Polar(double radius,double angle)=>new(Center+radius*Math.Cos(angle),Center+radius*Math.Sin(angle));
    private void Render(List<Operation> actions)
    {
        foreach(var shape in petals)canvas.Children.Remove(shape);foreach(var label in labels)canvas.Children.Remove(label);petals.Clear();labels.Clear();highlighted=-1;
        for(int i=0;i<actions.Count;i++)
        {
            double angle=-Math.PI/2+i*2*Math.PI/actions.Count,half=Math.PI/actions.Count,start=angle-half+.025,end=angle+half-.025;
            var geometry=new StreamGeometry();using(var context=geometry.Open())
            {
                context.BeginFigure(Polar(Inner,start),true,true);context.LineTo(Polar(Outer,start),true,false);
                context.ArcTo(Polar(Outer,end),new Size(Outer,Outer),0,end-start>Math.PI,SweepDirection.Clockwise,true,false);
                context.LineTo(Polar(Inner,end),true,false);context.ArcTo(Polar(Inner,start),new Size(Inner,Inner),0,end-start>Math.PI,SweepDirection.Counterclockwise,true,false);
            }
            geometry.Freeze();var shape=new ShapePath{Data=geometry,Fill=new SolidColorBrush(Color.FromRgb(242,243,245)),Stroke=Brushes.White,StrokeThickness=3};petals.Add(shape);canvas.Children.Add(shape);
            var label=new TextBlock{Text=actions[i].Label,FontSize=actions.Count>7?12:14,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Width=80,Height=44,IsHitTestVisible=false};
            Point p=Polar(119,angle);Canvas.SetLeft(label,p.X-40);Canvas.SetTop(label,p.Y-16);labels.Add(label);canvas.Children.Add(label);
        }
        UpdateLayout();
    }
    private int Hit(Point point){for(int i=0;i<petals.Count;i++)if(petals[i].Data.FillContains(point))return i;return-1;}
    private void Highlight(int index)
    {
        if(index==highlighted)return;highlighted=index;for(int i=0;i<petals.Count;i++){petals[i].Fill=i==index?UiTheme.Accent:new SolidColorBrush(Color.FromRgb(242,243,245));labels[i].SetValue(TextBlock.ForegroundProperty,i==index?Brushes.White:UiTheme.Ink);}hint.Text=index>=0?operations[index].Label:Catalog.PackingOnly(paths)?"暂不支持格式转换":"选操作后松手";
    }
}
