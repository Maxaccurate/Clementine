using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace ZestDrop;

internal sealed class TaskIndicatorWindow:Window
{
    private readonly ActivityIndicator indicator=new();
    private readonly DispatcherTimer dismiss=new(){Interval=TimeSpan.FromSeconds(3)};
    private ActivityIndicator.Session? session;
    public TaskIndicatorWindow()
    {
        Title="ZestDrop 处理进度";Icon=AppIcon.Window;Width=360;Height=142;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
        var card=UiTheme.Card(indicator,new Thickness(20,16,20,14));card.Margin=new Thickness(10);card.Effect=new DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=.12};Content=card;
        dismiss.Tick+=(_,_)=>{dismiss.Stop();Hide();};Closed+=(_,_)=>{dismiss.Stop();session?.Dispose();};
    }
    public IProgress<JobProgress> Begin(ConversionJob job)
    {
        dismiss.Stop();session?.Dispose();session=indicator.Begin(job.Action.StartsWith("convert:")?"正在转换格式":"正在处理文件",Path.GetFileName(job.Paths[0]));
        if(!IsVisible)Show();
        Native.GetCursorPos(out var cursor);var area=Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X,cursor.Y)).WorkingArea;
        var handle=new WindowInteropHelper(this).Handle;Native.SetWindowPos(handle,new IntPtr(-1),area.Right-1,area.Bottom-1,0,0,0x0001|0x0010);double scale=Native.GetDpiForWindow(handle)/96.0;
        int width=(int)Math.Round(Width*scale),height=(int)Math.Round(Height*scale),gap=(int)Math.Round(12*scale);
        Native.SetWindowPos(handle,new IntPtr(-1),area.Right-width-gap,area.Bottom-height-gap,width,height,0x0010|0x0040);
        return session;
    }
    public void Finish(string title,string detail)
    {
        session?.Dispose();session=null;indicator.Complete(title,detail);dismiss.Start();
    }
}
