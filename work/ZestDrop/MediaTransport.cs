using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZestDrop;

internal sealed class MediaTransport:StackPanel,IDisposable
{
    private readonly MediaElement player;
    private readonly Slider seek;
    private readonly Button toggle=new(){Content="播放",Padding=new Thickness(15,8,15,8),Margin=new Thickness(0,0,10,0)};
    private readonly ComboBox mode=new(){MinWidth=120,Margin=new Thickness(0,0,12,0)};
    private readonly ComboBox files=new(){Margin=new Thickness(0,0,0,8)};
    private readonly TextBlock clock=new(){Foreground=UiTheme.Muted,VerticalAlignment=VerticalAlignment.Center};
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
        var row=new DockPanel{Margin=new Thickness(0,0,0,8)};row.Children.Add(toggle);row.Children.Add(mode);row.Children.Add(clock);Children.Add(row);Children.Add(seek);
        var volumeRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,7,0,0)};
        var mute=new CheckBox{Content="静音",VerticalAlignment=VerticalAlignment.Center};mute.Checked+=(_,_)=>{muted=true;player.IsMuted=true;};mute.Unchecked+=(_,_)=>{muted=false;player.IsMuted=false;};volumeRow.Children.Add(mute);
        var volume=new Slider{Minimum=0,Maximum=1,Value=.65,Width=100,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};volume.ValueChanged+=(_,_)=>player.Volume=volume.Value;player.Volume=volume.Value;System.Windows.Automation.AutomationProperties.SetName(volume,"播放音量");volumeRow.Children.Add(volume);Children.Add(volumeRow);Children.Add(hint);
        toggle.Click+=async(_,_)=>await Toggle();mode.SelectionChanged+=(_,_)=>{Reset();ModeChanged?.Invoke();};
        seek.ValueChanged+=(_,_)=>{UpdateClock();if(!IsSynchronizing&&duration>0){requestedTime=seek.Value;seekDelay.Stop();seekDelay.Start();}};
        seekDelay.Tick+=async(_,_)=>{seekDelay.Stop();await Seek(requestedTime);};
        ticker.Tick+=(_,_)=>{if(ready&&IsPlaying&&!seekDelay.IsEnabled&&!IsSynchronizing){IsSynchronizing=true;seek.Value=Math.Clamp(localTimeline?player.Position.TotalSeconds:segment.ToSource(player.Position.TotalSeconds),0,seek.Maximum);IsSynchronizing=false;UpdateClock();}};
        player.MediaOpened+=(_,_)=>
        {
            if(disposed||!opening)return;opening=false;ready=true;
            double length=player.NaturalDuration.HasTimeSpan?player.NaturalDuration.TimeSpan.TotalSeconds:segment.Length;
            segment=segment with{Length=length};if(native){duration=length;IsSynchronizing=true;seek.Maximum=Math.Max(.001,duration);IsSynchronizing=false;}
            player.Position=TimeSpan.FromSeconds(localTimeline?Math.Clamp(requestedTime,0,length):segment.ToMedia(requestedTime));
            if(desiredPlay){player.Play();IsPlaying=true;ticker.Start();}else{player.Pause();IsPlaying=false;}
            player.IsMuted=muted;toggle.IsEnabled=true;toggle.Content=IsPlaying?"暂停":"播放";ViewChanged?.Invoke();UpdateClock();
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
    private void UpdateClock()=>clock.Text=$"{MediaSegment.Format(seek.Value)} / {MediaSegment.Format(seek.Maximum)}";
    private void Reset(){CancelPending();Pause();ready=opening=false;player.Source=null;toggle.IsEnabled=true;HasVideo=false;localTimeline=false;SetDuration(duration);hint.Text=EffectSelected?"效果播放为当前位置最多 30 秒；保存会处理完整文件或所选时段。":"可播放完整原文件；拖动进度条定位。";ViewChanged?.Invoke();}
    public void InvalidateEffect(){if(EffectSelected){CancelPending();Pause();ready=opening=false;player.Source=null;toggle.IsEnabled=true;HasVideo=false;ViewChanged?.Invoke();}}
    public void Pause(){player.Pause();IsPlaying=false;desiredPlay=false;ticker.Stop();toggle.Content="播放";if(ready){IsSynchronizing=true;seek.Value=Math.Clamp(localTimeline?player.Position.TotalSeconds:segment.ToSource(player.Position.TotalSeconds),0,seek.Maximum);IsSynchronizing=false;UpdateClock();}}
    public void Stop(){CancelPending();player.Stop();player.Source=null;ready=opening=false;IsPlaying=false;HasVideo=false;ticker.Stop();toggle.IsEnabled=true;toggle.Content="播放";ViewChanged?.Invoke();}
    private void CancelPending(){version++;pending?.Cancel();seekDelay.Stop();}
    public async Task Toggle()
    {
        if(disposed)return;if(IsPlaying){Pause();return;}
        if(ready){if(player.Position.TotalSeconds>=segment.Length-.05)player.Position=TimeSpan.Zero;player.Play();desiredPlay=IsPlaying=true;toggle.Content="暂停";ticker.Start();ViewChanged?.Invoke();return;}
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
        desiredPlay=autoplay;toggle.IsEnabled=false;toggle.Content="载入…";player.Stop();player.Source=null;player.IsMuted=true;ready=false;opening=false;ticker.Stop();
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
