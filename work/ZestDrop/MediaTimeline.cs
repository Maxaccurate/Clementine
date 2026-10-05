using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ZestDrop;

internal sealed class MediaTimeline:FrameworkElement
{
    private double duration=1,position,start,end=1;
    private int dragging;
    public event Action<double>? SeekRequested;
    public event Action<double,double>? RangeChanged;
    public bool RangeEnabled{get;set;}
    public bool SelectionEnabled{get;set;}=true;
    public MediaTimeline(){Height=46;Cursor=Cursors.Hand;Focusable=true;ToolTip="点击定位；拖动橙色节点设置开始和结束";System.Windows.Automation.AutomationProperties.SetName(this,"播放进度与裁剪范围");}
    public void SetPosition(double value,double total){duration=Math.Max(.001,total);position=Math.Clamp(value,0,duration);InvalidateVisual();}
    public void SetRange(double first,double last){start=Math.Clamp(first,0,duration);end=Math.Clamp(last,0,duration);InvalidateVisual();}
    private double X(double value)=>12+Math.Clamp(value/duration,0,1)*Math.Max(1,ActualWidth-24);
    private double Time(double x)=>Math.Clamp((x-12)/Math.Max(1,ActualWidth-24)*duration,0,duration);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double width=Math.Max(1,ActualWidth-24),y=19;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(67,70,78)),null,new Rect(12,y-2,width,4),2,2);
        if(RangeEnabled&&end>=start)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(141,70,35)),null,new Rect(X(start),y-4,Math.Max(1,X(end)-X(start)),8),3,3);
            foreach(double t in new[]{start,end})
            {
                dc.DrawRoundedRectangle(UiTheme.Accent,null,new Rect(X(t)-4,y-11,8,24),3,3);
                dc.DrawLine(new Pen(Brushes.White,1),new Point(X(t),y-6),new Point(X(t),y+6));
            }
            DrawLabel(dc,"开始 "+MediaSegment.Format(start),12,31);
            DrawLabel(dc,"结束 "+MediaSegment.Format(end),Math.Max(12,ActualWidth-94),31);
        }
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255,103,36)),null,new Rect(12,y-2,Math.Max(0,X(position)-12),4),2,2);
        dc.DrawEllipse(Brushes.White,null,new Point(X(position),y),5,5);
    }
    private void DrawLabel(DrawingContext dc,string text,double x,double y)
    {
        var label=new FormattedText(text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),10,new SolidColorBrush(Color.FromRgb(213,215,221)),VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(label,new Point(x,y));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);Focus();var p=e.GetPosition(this);
        bool endpoints=RangeEnabled&&SelectionEnabled&&(p.Y<14||p.Y>25);double first=Math.Abs(p.X-X(start)),last=Math.Abs(p.X-X(end));
        dragging=endpoints&&Math.Min(first,last)<12?(first<=last?1:2):3;
        CaptureMouse();Apply(p.X);e.Handled=true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);var p=e.GetPosition(this);if(dragging!=0)Apply(p.X);else ToolTip=RangeEnabled&&p.Y<14&&Math.Abs(p.X-X(start))<12?"拖动开始节点":RangeEnabled&&p.Y<14&&Math.Abs(p.X-X(end))<12?"拖动结束节点":MediaSegment.Format(Time(p.X));
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){base.OnMouseLeftButtonUp(e);dragging=0;ReleaseMouseCapture();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){base.OnLostMouseCapture(e);dragging=0;}
    private void Apply(double x)
    {
        double value=Time(x);
        if(dragging==1){start=TrimRange.Start(value,end,duration);RangeChanged?.Invoke(start,end);}
        else if(dragging==2){end=TrimRange.End(value,start,duration);RangeChanged?.Invoke(start,end);}
        else{position=value;SeekRequested?.Invoke(value);}
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(e.Key is Key.Left or Key.Right){double step=Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?.1:1;position=Math.Clamp(position+(e.Key==Key.Left?-step:step),0,duration);SeekRequested?.Invoke(position);InvalidateVisual();e.Handled=true;}
    }
}
