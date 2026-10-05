using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using ShapePath=System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace ZestDrop;

internal sealed class MediaTransport:StackPanel,IDisposable
{
    private readonly MediaElement player;
    private readonly Slider seek;
    private readonly Button toggle=IconButton("播放 / 暂停");
    private readonly Button sound=IconButton("静音 / 取消静音");
    private readonly Button modeButton=new(){Padding=new Thickness(10,5,10,5),MinHeight=28,FontSize=11,Background=new SolidColorBrush(Color.FromRgb(43,46,53)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};
    private readonly MediaTimeline rail=new();
    public Border Controls{get;private set;}=null!;
    public event Action<double,double>? RangeChanged;
    public void SetTrimRange(double first,double last){rail.RangeEnabled=true;rail.SetRange(first,last);}
    private static Button IconButton(string label)
    {
        var b=new Button{Width=34,MinHeight=30,Padding=new Thickness(7),Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0),ToolTip=label};System.Windows.Automation.AutomationProperties.SetName(b,label);return b;
    }
    private static ShapePath Glyph(string data)=>new(){Data=Geometry.Parse(data),Fill=Brushes.White,Width=18,Height=18,Stretch=Stretch.Uniform};
    private void PaintPlay()=>toggle.Content=Glyph(IsPlaying?"M4,3 H10 V21 H4Z M14,3 H20 V21 H14Z":"M5,3 L21,12 L5,21Z");
    private readonly ComboBox mode=new(){MinWidth=120,Margin=new Thickness(0,0,12,0)};
    private readonly ComboBox files=new(){Margin=new Thickness(0,0,0,8)};
    private readonly TextBlock clock=new(){Foreground=Brushes.White,FontSize=11,VerticalAlignment=VerticalAlignment.Center};
    private readonly TextBlock hint=new(){Foreground=UiTheme.Muted,FontSize=11,Margin=new Thickness(0,6,0,0),TextWrapping=TextWrapping.Wrap};
    private readonly string[] paths;
    private readonly bool audio;
    private readonly Func<bool,string,double,CancellationToken,Task<JsonElement>> prepare;
    private readonly CancellationToken lifetime;
    private readonly Action<string> message;
    private readonly DispatcherTimer ticker=new(){Interval=TimeSpan.FromMilliseconds(200)};
    private readonly DispatcherTimer seekDelay=new(){Interval=TimeSpan.FromMilliseconds(180)};
    private CancellationTokenSource? pending;
    private MediaSegment segment=new(0,0);
    private double duration,requestedTime;
    private bool ready,desiredPlay,native,opening,disposed,localTimeline,muted;
    private int version;
    public bool IsPlaying{get;private set;}
    public bool IsSynchronizing{get;private set;}
    public bool EffectSelected=>mode.SelectedIndex==1;
    public bool HasVideo{get;private set;}
    public event Action? ViewChanged;
    public event Action? ModeChanged;

    public MediaTransport(MediaElement player,Slider seek,string[] paths,bool audio,bool effects,Func<bool,string,double,CancellationToken,Task<JsonElement>> prepare,CancellationToken lifetime,Action<string> message)
    {
        this.player=player;this.seek=seek;this.paths=paths;this.audio=audio;this.prepare=prepare;this.lifetime=lifetime;this.message=message;Margin=new Thickness(0,10,0,8);
        mode.Items.Add("原文件");if(effects)mode.Items.Add("处理效果");mode.SelectedIndex=0;
        if(paths.Length>1){foreach(string path in paths)files.Items.Add(Path.GetFileName(path));files.SelectedIndex=0;Children.Add(files);files.SelectionChanged+=(_,_)=>Reset();}
        var panel=new StackPanel{Margin=new Thickness(12,0,12,8)};panel.Children.Add(rail);
        rail.SeekRequested+=time=>seek.Value=time;rail.RangeChanged+=(first,last)=>RangeChanged?.Invoke(first,last);
        var row=new DockPanel{LastChildFill=true};
        var left=new StackPanel{Orientation=Orientation.Horizontal};left.Children.Add(toggle);left.Children.Add(sound);
        var volume=new Slider{Minimum=0,Maximum=1,Value=.65,Width=65,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(1,0,12,0)};
        volume.Style=(Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid Height="20"><Track x:Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="White" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="#626772" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width="10" Height="10"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="White"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
        volume.ValueChanged+=(_,_)=>player.Volume=volume.Value;player.Volume=volume.Value;System.Windows.Automation.AutomationProperties.SetName(volume,"播放音量");left.Children.Add(volume);left.Children.Add(clock);row.Children.Add(left);
        modeButton.Content="原文件 ▾";modeButton.Visibility=effects?Visibility.Visible:Visibility.Collapsed;modeButton.Click+=(_,_)=>mode.SelectedIndex=EffectSelected?0:1;DockPanel.SetDock(modeButton,Dock.Right);row.Children.Insert(0,modeButton);panel.Children.Add(row);
        sound.Content=Glyph("M2,8 H7 L13,3 V21 L7,16 H2Z M16,7 L19,7 L19,17 L16,17Z");sound.Click+=(_,_)=>{muted=!muted;player.IsMuted=muted;sound.Content=Glyph(muted?"M2,8 H7 L13,3 V21 L7,16 H2Z M16,5 L19,5 L22,20 L19,20Z":"M2,8 H7 L13,3 V21 L7,16 H2Z M16,7 L19,7 L19,17 L16,17Z");};
        Controls=new Border{Background=new SolidColorBrush(Color.FromRgb(19,21,26)),Child=panel};Children.Add(hint);PaintPlay();
        toggle.Click+=async(_,_)=>await Toggle();mode.SelectionChanged+=(_,_)=>{Reset();modeButton.Content=EffectSelected?"处理效果 ▾":"原文件 ▾";rail.SelectionEnabled=!EffectSelected;ModeChanged?.Invoke();};
        seek.ValueChanged+=(_,_)=>{UpdateClock();if(!IsSynchronizing&&duration>0){requestedTime=seek.Value;seekDelay.Stop();seekDelay.Start();}};
        seekDelay.Tick+=async(_,_)=>{seekDelay.Stop();await Seek(requestedTime);};
        ticker.Tick+=(_,_)=>{if(ready&&IsPlaying&&!seekDelay.IsEnabled&&!IsSynchronizing){IsSynchronizing=true;seek.Value=Math.Clamp(localTimeline?player.Position.TotalSeconds:segment.ToSource(player.Position.TotalSeconds),0,seek.Maximum);IsSynchronizing=false;UpdateClock();}};
        player.MediaOpened+=(_,_)=>
        {
            if(disposed||!opening)return;opening=false;ready=true;
            double length=player.NaturalDuration.HasTimeSpan?player.NaturalDuration.TimeSpan.TotalSeconds:segment.Length;
            segment=segment with{Length=length};if(native){if(duration<=0||paths.Length>1)duration=length;IsSynchronizing=true;seek.Maximum=Math.Max(.001,duration);IsSynchronizing=false;}
            player.Position=TimeSpan.FromSeconds(localTimeline?Math.Clamp(requestedTime,0,length):segment.ToMedia(requestedTime));
            if(desiredPlay){player.Play();IsPlaying=true;ticker.Start();}else{player.Pause();IsPlaying=false;}
            player.IsMuted=muted;toggle.IsEnabled=true;PaintPlay();ViewChanged?.Invoke();UpdateClock();
        };
        player.MediaEnded+=(_,_)=>{Pause();if(ready){IsSynchronizing=true;seek.Value=Math.Clamp(localTimeline?segment.Length:segment.End,0,seek.Maximum);IsSynchronizing=false;UpdateClock();}if(EffectSelected)message("当前效果片段已播放完，可拖动进度条预览其他位置。");};
        player.MediaFailed+=async(_,e)=>
        {
            if(disposed||player.Source==null)return;
            if(native){native=false;ready=opening=false;await Load(desiredPlay,true);}
            else{Pause();toggle.IsEnabled=true;opening=false;message("播放失败："+e.ErrorException.Message);}
        };
        UpdateClock();
    }
    private string PathToPlay=>paths[Math.Max(0,files.SelectedIndex)];
    public void SetDuration(double value){duration=Math.Max(0,value);IsSynchronizing=true;seek.Maximum=Math.Max(.001,duration);IsSynchronizing=false;UpdateClock();}
    private void UpdateClock(){clock.Text=$"{MediaSegment.Format(seek.Value)} / {MediaSegment.Format(seek.Maximum)}";rail.SetPosition(seek.Value,seek.Maximum);}
    private void Reset(){CancelPending();Pause();ready=opening=false;player.Source=null;toggle.IsEnabled=true;HasVideo=false;localTimeline=false;SetDuration(duration);hint.Text=EffectSelected?"效果播放为当前位置最多 30 秒；保存会处理完整文件或所选时段。":"可播放完整原文件；拖动进度条定位。";ViewChanged?.Invoke();}
    public void InvalidateEffect(){if(EffectSelected){CancelPending();Pause();ready=opening=false;player.Source=null;toggle.IsEnabled=true;HasVideo=false;ViewChanged?.Invoke();}}
    public void Pause(){player.Pause();IsPlaying=false;desiredPlay=false;ticker.Stop();PaintPlay();if(ready){IsSynchronizing=true;seek.Value=Math.Clamp(localTimeline?player.Position.TotalSeconds:segment.ToSource(player.Position.TotalSeconds),0,seek.Maximum);IsSynchronizing=false;UpdateClock();}}
    public void Stop(){CancelPending();player.Stop();player.Source=null;ready=opening=false;IsPlaying=false;HasVideo=false;ticker.Stop();toggle.IsEnabled=true;PaintPlay();ViewChanged?.Invoke();}
    private void CancelPending(){version++;pending?.Cancel();seekDelay.Stop();}
    public async Task Toggle()
    {
        if(disposed)return;if(IsPlaying){Pause();return;}
        if(ready){if(player.Position.TotalSeconds>=segment.Length-.05)player.Position=TimeSpan.Zero;player.Play();desiredPlay=IsPlaying=true;PaintPlay();ticker.Start();ViewChanged?.Invoke();return;}
        requestedTime=seek.Value;await Load(true,false);
    }
    public async Task Seek(double time)
    {
        if(disposed||opening)return;
        if(ready&&(localTimeline||segment.Contains(time))){player.Position=TimeSpan.FromSeconds(localTimeline?time:segment.ToMedia(time));return;}
        requestedTime=time;await Load(IsPlaying,false);
    }
    private async Task Load(bool autoplay,bool compatibility)
    {
        CancelPending();int current=version;var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime);pending=cancellation;
        desiredPlay=autoplay;toggle.IsEnabled=false;toggle.Content=new TextBlock{Text="…",Foreground=Brushes.White,FontSize=16};player.Stop();player.Source=null;player.IsMuted=true;ready=false;opening=false;ticker.Stop();
        try
        {
            if(!EffectSelected&&!compatibility)
            {
                native=true;HasVideo=!audio;localTimeline=false;segment=new(0,duration);opening=true;player.Source=new Uri(System.IO.Path.GetFullPath(PathToPlay));
                hint.Text="原文件播放 · 可定位完整文件";player.Play();return;
            }
            native=false;var info=await prepare(EffectSelected,PathToPlay,requestedTime,cancellation.Token);if(current!=version||disposed)return;
            double start=info.TryGetProperty("Start",out var s)?s.GetDouble():0;
            double rate=info.TryGetProperty("Rate",out var r)?r.GetDouble():1;
            double length=info.TryGetProperty("Duration",out var d)?d.GetDouble():30;
            segment=new(start,length,rate);localTimeline=info.TryGetProperty("LinearTimeline",out var linear)&&!linear.GetBoolean();
            if(localTimeline){requestedTime=0;IsSynchronizing=true;seek.Maximum=Math.Max(.001,length);seek.Value=0;IsSynchronizing=false;}
            HasVideo=info.TryGetProperty("Kind",out var kind)?kind.GetString()=="video":!audio;
            opening=true;player.Source=new Uri(info.GetProperty("Preview").GetString()!);player.Play();
            hint.Text=EffectSelected?$"效果片段：{MediaSegment.Format(start)}–{MediaSegment.Format(segment.End)} · 保存处理完整选择":"原文件兼容播放 · 可定位完整文件";
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(current==version){opening=false;toggle.IsEnabled=true;message("播放准备失败："+ex.Message);}}
        finally{cancellation.Dispose();if(ReferenceEquals(pending,cancellation))pending=null;}
    }
    public void Dispose(){disposed=true;Stop();ticker.Stop();seekDelay.Stop();}
}
