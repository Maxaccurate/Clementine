using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path=System.IO.Path;

namespace DesktopDrop;

internal sealed class ToolWindow:Window
{
    private readonly string[] paths;
    private readonly Operation operation;
    private readonly Func<ConversionJob,CancellationToken,IProgress<JobProgress>?,Task<BatchResult>> submit;
    private readonly Dictionary<string,FrameworkElement> controls=[];
    private readonly Dictionary<string,TextBlock> fieldLabels=[];
    private readonly TextBlock status=new(),details=new();
    private readonly Image image=new(){Stretch=Stretch.Uniform};
    private readonly Canvas overlay=new(){Background=Brushes.Transparent};
    private readonly Grid previewHost=new();
    private readonly MediaElement player=new(){LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Stop};
    private readonly ListBox order=new(){Height=120};
    private readonly Slider timeline=new(){Minimum=0,Maximum=1};
    private readonly TextBlock positionLabel=new(){Foreground=UiTheme.Muted,Margin=new Thickness(0,8,0,4)};
    private readonly Rectangle selection=new(){Stroke=Brushes.DarkOrange,StrokeThickness=2,Fill=new SolidColorBrush(Color.FromArgb(35,255,120,20)),Visibility=Visibility.Collapsed};
    private readonly List<Dictionary<string,object>> regions=[];
    private readonly List<Dictionary<string,double>> audioRanges=[];
    private readonly ListBox regionList=new(){Height=82};
    private readonly CancellationTokenSource closing=new();
    private readonly SemaphoreSlim previewGate=new(1,1);
    private readonly DispatcherTimer previewDelay;
    private string folder=Path.Combine(AppContext.BaseDirectory,"logs","preview-"+Guid.NewGuid().ToString("N"));
    private string? originalPreview;
    private int originalWidth,originalHeight,pages;
    private double totalDuration,fps=30;
    private Point? selectionStart;
    private bool loaded,playing,showingOriginal=true;
    private bool rangeEdited;
    private bool playingOriginal;
    private int previewVersion;
    private string metadataText="";
    private Button saveButton=null!,cancelButton=null!,revealButton=null!;
    private readonly ActivityIndicator activity=new();
    private FrameworkElement bodyPanel=null!;
    private CancellationTokenSource? saving,previewCancellation;
    private string? lastOutput;
    private readonly ComboBox framePicker=new(){Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,10)};
    private bool syncingRegion;
    private int imageFrame=-1;
    private bool VisualPreview=>operation.Id is "editImage"or"frameImage"or"cropImage"or"cropVideo"or"redactImage"or"redactVideo"or"organizePDF"or"createCollage";
    private bool ProcessedPlayback=>operation.Id is "normalizeAudio"or"audioChannels"or"redactAudio"or"trimAudio"or"trimVideo"or"changeVideoSpeed"or"redactVideo";
    private bool HasTimeline=>operation.Id is "cropVideo"or"redactVideo"or"videoSnapshots"or"trimVideo"or"trimAudio"or"redactAudio";
    private bool RefreshFrame=>VisualPreview||HasTimeline&&Catalog.Category(paths[0])=="video";
    private Button? compareButton,playButton;

    public ToolWindow(string[] paths,Operation operation,Func<ConversionJob,CancellationToken,IProgress<JobProgress>?,Task<BatchResult>> submit)
    {
        this.paths=paths;this.operation=operation;this.submit=submit;
        Title=operation.Label+" · "+Path.GetFileName(paths[0]);Width=1040;Height=800;MinWidth=850;MinHeight=640;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(248,249,251));FontFamily=new FontFamily("Segoe UI");FontSize=13;
        previewDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(550)};previewDelay.Tick+=async(_,_)=>{previewDelay.Stop();await UpdatePreview();};
        UiTheme.Apply(this);Content=UiTheme.Frame(this,Build());Loaded+=async(_,_)=>await Initialize();
        Closing+=(_,e)=>{if(saving!=null){e.Cancel=true;status.Text="处理中。请先点击取消，等待任务停止后关闭窗口。";}};
        PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Control){e.Handled=true;await Save();}};
        player.MediaEnded+=(_,_)=>{playing=false;if(playButton!=null)playButton.Content=PlaybackLabel;};
        player.MediaFailed+=(_,e)=>{status.Text="播放失败："+e.ErrorException.Message;playing=false;if(playButton!=null)playButton.Content=PlaybackLabel;};
        Closed+=async(_,_)=>
        {
            closing.Cancel();previewCancellation?.Cancel();previewDelay.Stop();player.Stop();player.Source=null;
            await Task.Delay(1000);
            try
            {
                string root=Path.GetFullPath(Journal.DirectoryPath)+Path.DirectorySeparatorChar;
                if(Path.GetFullPath(folder).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(folder).StartsWith("preview-")&&Directory.Exists(folder))Directory.Delete(folder,true);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        };
    }
    private UIElement Build()
    {
        var root=new Grid{Margin=new Thickness(24,8,24,22)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new StackPanel();header.Children.Add(new TextBlock{Text=operation.Label,FontSize=26,FontWeight=FontWeights.SemiBold});header.Children.Add(new TextBlock{Text=paths.Length==1?Path.GetFileName(paths[0]):$"已选 {paths.Length} 个文件",Foreground=UiTheme.Muted,Margin=new Thickness(0,6,0,20)});root.Children.Add(header);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(340)});bodyPanel=body;Grid.SetRow(body,1);root.Children.Add(body);
        var left=new StackPanel{Margin=new Thickness(0,0,0,0)};
        left.Children.Add(new TextBlock{Text=VisualPreview?"效果预览":"源文件预览",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        left.Children.Add(framePicker);framePicker.SelectionChanged+=async(_,_)=>{if(loaded&&framePicker.SelectedItem is ComboBoxItem item){imageFrame=(int)item.Tag;regions.Clear();regionList.Items.Clear();await Initialize();SchedulePreview();}};
        previewHost.Height=300;previewHost.Background=new SolidColorBrush(Color.FromRgb(244,245,247));previewHost.Children.Add(image);previewHost.Children.Add(player);player.Visibility=Visibility.Collapsed;previewHost.Children.Add(overlay);overlay.Children.Add(selection);left.Children.Add(previewHost);
        var busyOverlay=new Border{Background=new SolidColorBrush(Color.FromArgb(235,244,245,247))};
        var busyCard=UiTheme.Card(activity,new Thickness(22,18,22,18));busyCard.Width=290;busyCard.MaxWidth=380;busyCard.Margin=new Thickness(16);busyCard.VerticalAlignment=VerticalAlignment.Center;busyCard.HorizontalAlignment=HorizontalAlignment.Center;busyOverlay.Child=busyCard;
        busyOverlay.SetBinding(VisibilityProperty,new Binding("Visibility"){Source=activity});previewHost.Children.Add(busyOverlay);
        overlay.MouseLeftButtonDown+=BeginSelection;overlay.MouseMove+=MoveSelection;overlay.MouseLeftButtonUp+=EndSelection;
        var previewButtons=new WrapPanel{Margin=new Thickness(0,8,0,8)};
        if(VisualPreview)
        {
            compareButton=Button("查看效果",async()=>
            {
                if(showingOriginal){await UpdatePreview();}
                else if(originalPreview!=null){player.Stop();player.Visibility=Visibility.Collapsed;SetImage(originalPreview);showingOriginal=true;compareButton!.Content="查看效果";status.Text="正在查看原始画面。";}
            });previewButtons.Children.Add(compareButton);
        }
        if(ProcessedPlayback&&paths.Length==1){playButton=Button(PlaybackLabel,()=>Play(false));previewButtons.Children.Add(playButton);}
        left.Children.Add(previewButtons);
        if(HasTimeline)
        {
            left.Children.Add(positionLabel);left.Children.Add(timeline);System.Windows.Automation.AutomationProperties.SetName(timeline,"预览位置（秒）");timeline.ValueChanged+=(_,_)=>{positionLabel.Text=$"预览位置  {timeline.Value:0.000} / {totalDuration:0.000} 秒";if(loaded){Set("time",timeline.Value.ToString("0.000",CultureInfo.InvariantCulture));if(RefreshFrame)SchedulePreview();}};
            var frameButtons=new WrapPanel();if(operation.Id=="videoSnapshots"){frameButtons.Children.Add(Button("上一帧",()=>StepFrame(-1)));frameButtons.Children.Add(Button("下一帧",()=>StepFrame(1)));}
            if((operation.Fields??[]).Any(f=>f.Name=="start")){frameButtons.Children.Add(Button("设为开始",()=>{Set("start",timeline.Value.ToString("0.000",CultureInfo.InvariantCulture));return Task.CompletedTask;}));frameButtons.Children.Add(Button("设为结束",()=>{Set("end",timeline.Value.ToString("0.000",CultureInfo.InvariantCulture));return Task.CompletedTask;}));}left.Children.Add(frameButtons);
        }
        if(operation.Id=="cropVideo")left.Children.Add(new TextBlock{Text="只裁剪画面；保留完整时长和原有音轨。",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,4)});
        if(operation.Id is "cropImage" or "cropVideo" or "redactImage" or "redactVideo")
        {
            bool cropping=operation.Id is "cropImage"or"cropVideo";
            left.Children.Add(new TextBlock{Text=cropping?"在预览上拖动选择裁剪范围。":"在预览上拖动选区，可添加多个打码区域。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});
            left.Children.Add(Button(cropping?"重置裁剪":"清空覆盖区域",()=>
            {
                regions.Clear();regionList.Items.Clear();Set("regions","");selection.Visibility=Visibility.Collapsed;
                if(cropping){Set("x","0");Set("y","0");Set("width",originalWidth.ToString());Set("height",originalHeight.ToString());Set("ratio","free");if(originalPreview!=null){SetImage(originalPreview);showingOriginal=true;}}
                if(!cropping){Set("width","0");Set("height","0");rangeEdited=false;if(originalPreview!=null){SetImage(originalPreview);showingOriginal=true;}status.Text="覆盖区域已清空。请拖动添加新区域。";}else SchedulePreview();return Task.CompletedTask;
            }));
            if(operation.Id is "redactImage"or"redactVideo")
            {
                left.Children.Add(regionList);
                regionList.SelectionChanged+=(_,_)=>
                {
                    if(regionList.SelectedItem is ListBoxItem item && item.Tag is Dictionary<string,object> r)
                    {syncingRegion=true;foreach(string key in new[]{"x","y","width","height","start","end","style","blockSize"})if(r.TryGetValue(key,out var value))Set(key,value.ToString()!);syncingRegion=false;}
                };
                var regionButtons=new WrapPanel();regionButtons.Children.Add(Button("移除选中区域",()=>{if(regionList.SelectedItem is ListBoxItem item&&item.Tag is Dictionary<string,object> r){regions.Remove(r);regionList.Items.Remove(item);if(regions.Count==0){Set("width","0");Set("height","0");rangeEdited=false;if(originalPreview!=null){SetImage(originalPreview);showingOriginal=true;}}else SchedulePreview();}return Task.CompletedTask;}));left.Children.Add(regionButtons);
            }
        }
        if(operation.Id=="redactAudio")
        {
            left.Children.Add(new TextBlock{Text="在波形上拖动选择时段；可添加多个蜂鸣范围。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});
            left.Children.Add(Button("清空蜂鸣范围",()=>{audioRanges.Clear();regionList.Items.Clear();Set("ranges","");rangeEdited=false;selection.Visibility=Visibility.Collapsed;status.Text="蜂鸣时段已清空。";return Task.CompletedTask;}));
        }
        if(operation.Id=="redactAudio")
        {
            left.Children.Add(regionList);left.Children.Add(Button("移除选中时段",()=>{if(regionList.SelectedItem is ListBoxItem item&&item.Tag is Dictionary<string,double> r){audioRanges.Remove(r);regionList.Items.Remove(item);rangeEdited=audioRanges.Count>0;}return Task.CompletedTask;}));
            regionList.SelectionChanged+=(_,_)=>{if(regionList.SelectedItem is ListBoxItem item&&item.Tag is Dictionary<string,double> r){syncingRegion=true;Set("start",r["start"].ToString(CultureInfo.InvariantCulture));Set("end",r["end"].ToString(CultureInfo.InvariantCulture));syncingRegion=false;}};
        }
        if(operation.Id=="normalizeAudio")left.Children.Add(Button("分析原始与处理后响度",async()=>
        {
            using var busy=activity.Begin("正在分析响度",Path.GetFileName(paths[0]));try{status.Text="分析响度…";var info=await Backend.Preview(Capture(),folder,closing.Token,"--analyze");status.Text=$"原始 {info.GetProperty("Input").GetString()} LUFS · 处理后 {info.GetProperty("Output").GetString()} LUFS";}catch(Exception ex){status.Text="分析："+ex.Message;}
        }));
        left.Children.Add(new ScrollViewer{Content=details,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});details.TextWrapping=TextWrapping.Wrap;details.Foreground=Brushes.DimGray;details.Margin=new Thickness(0,8,0,8);
        if(operation.Ordered||operation.Id=="organizePDF")
        {
            left.Children.Add(order);var reorder=new WrapPanel();reorder.Children.Add(Button("上移",()=>{MoveItem(-1);return Task.CompletedTask;}));reorder.Children.Add(Button("下移",()=>{MoveItem(1);return Task.CompletedTask;}));
            reorder.Children.Add(Button("移出选择",()=>{if(order.SelectedIndex>=0)order.Items.RemoveAt(order.SelectedIndex);return Task.CompletedTask;}));
            if(operation.Id=="organizePDF")reorder.Children.Add(Button("复制页",()=>{if(order.SelectedItem is ListBoxItem item){int index=order.SelectedIndex;order.Items.Insert(index+1,new ListBoxItem{Content=item.Content,Tag=item.Tag});}return Task.CompletedTask;}));
            left.Children.Add(reorder);order.SelectionChanged+=(_,_)=>{if(loaded&&VisualPreview)SchedulePreview();};
        }
        var previewCard=UiTheme.Card(new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},new Thickness(18));previewCard.Margin=new Thickness(0,0,16,0);body.Children.Add(previewCard);
        var parameters=new StackPanel();parameters.Children.Add(new TextBlock{Text="处理参数",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,10)});
        var advanced=new StackPanel();
        foreach(var field in operation.Fields??[])
        {
            var fieldHost=field.Kind=="json"?advanced:parameters;var fieldLabel=new TextBlock{Text=field.Label,Margin=new Thickness(0,9,0,4),TextWrapping=TextWrapping.Wrap};fieldLabels[field.Name]=fieldLabel;fieldHost.Children.Add(fieldLabel);FrameworkElement control;
            if(field.Name=="metadata")control=new MetadataEditor();
            else if(field.Kind=="bool")control=new CheckBox{IsChecked=field.Default=="true",Content="启用"};
            else if(field.Kind=="choice")
            {
                var combo=new ComboBox();foreach(string choice in field.Choices??[])combo.Items.Add(new ComboBoxItem{Content=Choice(choice),Tag=choice});combo.SelectedIndex=Array.IndexOf(field.Choices??[],field.Default);control=combo;
            }
            else if(field.Kind=="password")control=new PasswordBox();
            else
            {
                var box=new TextBox{Text=field.Default,Padding=new Thickness(10,8,10,8),AcceptsReturn=field.Kind=="json",TextWrapping=field.Kind=="json"?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
                if(field.Kind=="json"){box.Height=105;box.FontFamily=new FontFamily("Consolas");}
                box.TextChanged+=(_,_)=>{if(loaded){if(new[]{"x","y","width","height","start","end"}.Contains(field.Name))rangeEdited=true;if(field.Name=="search")FilterMetadata(box.Text);else ParameterChanged();}};
                control=box;
            }
            controls[field.Name]=control;fieldHost.Children.Add(control);System.Windows.Automation.AutomationProperties.SetName(control,field.Label);
            if(control is ComboBox choiceControl)choiceControl.SelectionChanged+=(_,_)=>{if(loaded&&VisualPreview)ParameterChanged();};
            if(control is CheckBox toggle&&field.Name=="remove"){toggle.Checked+=(_,_)=>SetMetadataState();toggle.Unchecked+=(_,_)=>SetMetadataState();}
            if(field.Kind=="file")parameters.Children.Add(Button("选择图片…",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.heic;*.avif|所有文件|*.*"};if(dialog.ShowDialog(this)==true)Set(field.Name,dialog.FileName);return Task.CompletedTask;}));
        }
        if(operation.Id is "redactImage"or"redactVideo")
        {
            void UpdateRedactionFields(){bool mosaic=Get("style")=="pixelate";controls["blockSize"].Visibility=fieldLabels["blockSize"].Visibility=mosaic?Visibility.Visible:Visibility.Collapsed;if(controls.TryGetValue("color",out var color))color.Visibility=fieldLabels["color"].Visibility=Get("style")=="solid"?Visibility.Visible:Visibility.Collapsed;}
            ((ComboBox)controls["style"]).SelectionChanged+=(_,_)=>UpdateRedactionFields();UpdateRedactionFields();
        }
        if(controls.ContainsKey("ratio"))
        {
            var customRatio=new StackPanel{Margin=new Thickness(0,10,0,0)};
            customRatio.Children.Add(new TextBlock{Text="自定义比例（宽 : 高）",Margin=new Thickness(0,0,0,6)});
            var inputs=new Grid();inputs.ColumnDefinitions.Add(new ColumnDefinition());inputs.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});inputs.ColumnDefinitions.Add(new ColumnDefinition());
            foreach(string key in new[]{"ratioWidth","ratioHeight"}){parameters.Children.Remove(controls[key]);parameters.Children.Remove(fieldLabels[key]);}
            inputs.Children.Add(controls["ratioWidth"]);Grid.SetColumn(controls["ratioHeight"],2);inputs.Children.Add(controls["ratioHeight"]);
            var separator=new TextBlock{Text=":",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(separator,1);inputs.Children.Add(separator);customRatio.Children.Add(inputs);parameters.Children.Add(customRatio);
            void ShowCustomRatio()=>customRatio.Visibility=Get("ratio")=="custom"?Visibility.Visible:Visibility.Collapsed;
            ((ComboBox)controls["ratio"]).SelectionChanged+=(_,_)=>ShowCustomRatio();ShowCustomRatio();
        }
        if(advanced.Children.Count>0)parameters.Children.Add(new Expander{Header="高级参数（可选）",Content=advanced,Margin=new Thickness(0,16,0,0)});
        if((operation.Fields?.Length??0)==0)parameters.Children.Add(new TextBlock{Text="在左侧调整文件顺序，然后保存。",Foreground=UiTheme.Muted,TextWrapping=TextWrapping.Wrap});
        var scroll=UiTheme.Card(new ScrollViewer{Content=parameters,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},new Thickness(18));Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        var bottom=new Grid{Margin=new Thickness(0,18,0,0)};bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var feedback=new StackPanel{Margin=new Thickness(0,0,20,0),VerticalAlignment=VerticalAlignment.Center};status.Text="读取文件信息…";status.TextWrapping=TextWrapping.Wrap;status.Foreground=UiTheme.Muted;feedback.Children.Add(status);bottom.Children.Add(feedback);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);bottom.Children.Add(actions);
        revealButton=Button("打开输出位置",()=>{if(lastOutput!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+lastOutput+"\""){UseShellExecute=true});return Task.CompletedTask;});revealButton.Visibility=Visibility.Collapsed;actions.Children.Add(revealButton);
        cancelButton=Button("取消处理",()=>{saving?.Cancel();cancelButton.IsEnabled=false;status.Text="正在停止处理…";return Task.CompletedTask;});cancelButton.Visibility=Visibility.Collapsed;actions.Children.Add(cancelButton);
        saveButton=Button("保存新文件",Save);saveButton.IsEnabled=false;saveButton.Foreground=Brushes.White;saveButton.Background=UiTheme.Accent;saveButton.BorderThickness=new Thickness(0);saveButton.Padding=new Thickness(22,10,22,10);saveButton.ToolTip="Ctrl + Enter";actions.Children.Add(saveButton);Grid.SetRow(bottom,2);root.Children.Add(bottom);
        return root;
    }
    private static string Choice(string value)=>value switch{"solid"=>"纯色遮盖","blur"=>"模糊","pixelate"=>"马赛克","free"=>"自由","custom"=>"自定义","grid"=>"网格","row"=>"一行","column"=>"一列","featured"=>"主图布局","landscape"=>"横向","portrait"=>"纵向","square"=>"正方形","mono"=>"单声道","stereo"=>"双声道",_=>value};
    private string PlaybackLabel=>Catalog.Category(paths[0])=="audio"?"试听效果":operation.Id=="trimVideo"?"播放选中时段":"播放效果";
    private static Button Button(string text,Func<Task> click)
    {
        var button=new Button{Content=text,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,6,5)};button.Click+=async(_,_)=>await click();return button;
    }
    private async Task Initialize()
    {
        using var busy=activity.Begin("正在读取文件",Path.GetFileName(paths[0]));
        loaded=false;saveButton.IsEnabled=false;framePicker.IsEnabled=false;previewDelay.Stop();previewCancellation?.Cancel();previewVersion++;rangeEdited=false;if(operation.Ordered&&order.Items.Count==0)foreach(string path in paths)order.Items.Add(new ListBoxItem{Content=Path.GetFileName(path),Tag=path});
        try
        {
            var info=await Backend.Inspect(paths[0],folder,closing.Token,imageFrame);
            originalWidth=info.GetProperty("Width").GetInt32();originalHeight=info.GetProperty("Height").GetInt32();totalDuration=info.GetProperty("Duration").GetDouble();pages=info.GetProperty("Pages").GetInt32();
            double rate=info.GetProperty("FrameRate").GetDouble();if(rate>0)fps=rate;
            originalPreview=info.GetProperty("Preview").GetString();if(originalPreview!=null){SetImage(originalPreview);showingOriginal=true;}
            if(paths.Length==1&&info.TryGetProperty("Frames",out var frames)&&frames.GetInt32()>1)
            {
                if(framePicker.Items.Count==0){for(int i=0;i<frames.GetInt32();i++)framePicker.Items.Add(new ComboBoxItem{Content=$"第 {i+1} 帧",Tag=i});framePicker.SelectedIndex=info.GetProperty("Frame").GetInt32();}
                framePicker.Visibility=Visibility.Visible;imageFrame=info.GetProperty("Frame").GetInt32();
            }
            if(originalWidth>0){Set("width",originalWidth.ToString());Set("height",originalHeight.ToString());details.Text=$"{originalWidth} × {originalHeight} 像素";}
            if(totalDuration>0){timeline.Maximum=Math.Max(.001,totalDuration-.001);Set("end",totalDuration.ToString("0.000",CultureInfo.InvariantCulture));details.Text+=$"  · {totalDuration:0.00} 秒";if(Catalog.Category(paths[0])=="video")details.Text+=$" · {fps:0.##} fps";positionLabel.Text=$"预览位置  {timeline.Value:0.000} / {totalDuration:0.000} 秒";}
            metadataText=JsonSerializer.Serialize(info.GetProperty("Metadata"),new JsonSerializerOptions{WriteIndented=true});Set("metadata",metadataText);
            if(controls.TryGetValue("metadata",out var editor)&&editor is MetadataEditor table)table.Populate(info.GetProperty("Metadata"));
            if(operation.Id=="removeMetadata")details.Text="可搜索字段；关闭“移除全部”后可编辑元数据。";
            if(operation.Id=="organizePDF")
            {
                for(int i=1;i<=pages;i++)order.Items.Add(new ListBoxItem{Content=$"第 {i} 页",Tag=i});
                Set("pageOrder",string.Join(",",Enumerable.Range(1,pages)));details.Text=$"共 {pages} 页；调整列表或输入页码顺序。";
            }
            status.Text=imageFrame>=0?"此图片包含多个图像帧。仅导出当前帧，原始文件保留。":"调整参数后保存；新文件保存在原目录。";
        }
        catch(OperationCanceledException){}
        catch(Exception ex){status.Text="预览不可用："+ex.Message+"。仍可填写参数。";}
        loaded=true;saveButton.IsEnabled=true;framePicker.IsEnabled=true;SetMetadataState();
    }
    private void Set(string name,string value)
    {
        if(!controls.TryGetValue(name,out var control))return;
        if(control is TextBox text)text.Text=value;
        if(control is ComboBox combo)foreach(ComboBoxItem item in combo.Items)if(item.Tag?.ToString()==value)combo.SelectedItem=item;
    }
    private string Get(string name,string fallback="")=>controls.TryGetValue(name,out var control)?control switch{MetadataEditor table=>table.ToJson(),TextBox text=>text.Text,CheckBox check=>check.IsChecked==true?"true":"false",ComboBox combo=>((ComboBoxItem?)combo.SelectedItem)?.Tag?.ToString()??fallback,PasswordBox pass=>pass.Password,_=>fallback}:fallback;
    private ConversionJob Capture()
    {
        var values=new Dictionary<string,string>();foreach(var field in operation.Fields??[])values[field.Name]=Get(field.Name,field.Default);
        if(values.TryGetValue("ratio",out var selectedRatio)&&selectedRatio!="custom"){values["ratioWidth"]="3";values["ratioHeight"]="2";}
        if(values.TryGetValue("style",out var selectedStyle)&&selectedStyle!="pixelate")values["blockSize"]="12";
        JobValidation.ResolveRatio(values);
        foreach(var field in operation.Fields??[])if(field.Kind=="number"&&!string.IsNullOrWhiteSpace(values[field.Name])&&!values[field.Name].Contains(':')&&!double.TryParse(values[field.Name],NumberStyles.Float,CultureInfo.InvariantCulture,out _))throw new ArgumentException(field.Label+"需要数字");
        values["fps"]=fps.ToString(CultureInfo.InvariantCulture);if(!values.ContainsKey("time"))values["time"]=timeline.Value.ToString(CultureInfo.InvariantCulture);
        if(regions.Count>0)values["regions"]=JsonSerializer.Serialize(regions);
        if(audioRanges.Count>0)values["ranges"]=JsonSerializer.Serialize(audioRanges);
        if(operation.Id is "redactImage"or"redactVideo"&&regions.Count==0&&string.IsNullOrWhiteSpace(Get("regions"))&&!rangeEdited)throw new ArgumentException("请先在预览上选择覆盖区域。");
        if(operation.Id=="redactAudio"&&audioRanges.Count==0&&string.IsNullOrWhiteSpace(Get("ranges"))&&!rangeEdited)throw new ArgumentException("请先在波形上选择蜂鸣时段。");
        string[] ordered=operation.Ordered?order.Items.Cast<ListBoxItem>().Select(x=>(string)x.Tag).ToArray():paths;
        if(ordered.Length==0)throw new ArgumentException("至少需要选择一个文件");
        if(operation.Id=="organizePDF")
        {
            string generated=string.Join(",",order.Items.Cast<ListBoxItem>().Select(x=>x.Tag));
            string initial=string.Join(",",Enumerable.Range(1,pages));
            if(order.Items.Count==0&&values["pageOrder"]==initial)throw new ArgumentException("至少需要保留一页。");
            if(values["pageOrder"]==initial)values["pageOrder"]=generated;
            if(order.SelectedItem is ListBoxItem selected)values["pageNumber"]=selected.Tag.ToString()!;
        }
        if(imageFrame>=0)values["imageFrame"]=imageFrame.ToString();var job=new ConversionJob(ordered,operation.Id,values);JobValidation.Check(job,originalWidth,originalHeight,totalDuration,pages);return job;
    }
    private void SchedulePreview(){previewDelay.Stop();previewDelay.Start();}
    private async Task UpdatePreview()
    {
        if(!loaded||closing.IsCancellationRequested||saving!=null||selectionStart!=null)return;int version=++previewVersion;previewCancellation?.Cancel();var cancellation=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);previewCancellation=cancellation;status.Text="更新效果中…";
        using var busy=activity.Begin("正在生成预览",Path.GetFileName(paths[0]));
        try
        {
            await previewGate.WaitAsync(cancellation.Token);try
            {
                var info=await Backend.Preview(Capture(),folder,cancellation.Token);
                if(version!=previewVersion)return;
                if(info.TryGetProperty("SourcePreview",out var source))originalPreview=source.GetString();
                var preview=info.GetProperty("Preview").GetString();if(preview!=null){player.Visibility=Visibility.Collapsed;player.Stop();playing=false;if(playButton!=null)playButton.Content=PlaybackLabel;SetImage(preview);selection.Visibility=Visibility.Collapsed;showingOriginal=false;if(compareButton!=null)compareButton.Content="查看原始";status.Text="预览已更新；保存后生成新文件。";}
            }finally{previewGate.Release();}
        }catch(OperationCanceledException){}catch(Exception ex){if(version==previewVersion)status.Text="预览："+ex.Message;}finally{cancellation.Dispose();if(ReferenceEquals(previewCancellation,cancellation))previewCancellation=null;}
    }
    private void SetImage(string path)
    {
        using var stream=File.OpenRead(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();image.Source=bitmap;image.Visibility=Visibility.Visible;
    }
    private void FilterMetadata(string search)
    {
        if(controls.TryGetValue("metadata",out var editor)&&editor is MetadataEditor table)table.Filter(search);
        details.Text="可搜索字段；关闭“移除全部”后可编辑元数据。";
    }
    private void MoveItem(int direction)
    {
        int index=order.SelectedIndex,next=index+direction;if(index<0||next<0||next>=order.Items.Count)return;var item=order.Items[index];order.Items.RemoveAt(index);order.Items.Insert(next,item);order.SelectedIndex=next;
    }
    private Point SourcePoint(Point point)
    {
        double scale=Math.Min(previewHost.ActualWidth/Math.Max(1,originalWidth),previewHost.ActualHeight/Math.Max(1,originalHeight));
        double offsetX=(previewHost.ActualWidth-originalWidth*scale)/2,offsetY=(previewHost.ActualHeight-originalHeight*scale)/2;
        return new Point(Math.Clamp((point.X-offsetX)/scale,0,originalWidth),Math.Clamp((point.Y-offsetY)/scale,0,originalHeight));
    }
    private void BeginSelection(object sender,MouseButtonEventArgs e)
    {
        bool audio=operation.Id is "redactAudio"or"trimAudio";
        if(!loaded||saving!=null)return;if(!audio&&(operation.Id is not("cropImage"or"cropVideo"or"redactImage"or"redactVideo")||originalWidth==0))return;
        if(!showingOriginal&&originalPreview!=null){SetImage(originalPreview);showingOriginal=true;}
        previewDelay.Stop();previewCancellation?.Cancel();selectionStart=e.GetPosition(previewHost);overlay.CaptureMouse();selection.Visibility=Visibility.Visible;Canvas.SetLeft(selection,selectionStart.Value.X);Canvas.SetTop(selection,selectionStart.Value.Y);selection.Width=selection.Height=0;
    }
    private void MoveSelection(object sender,MouseEventArgs e)
    {
        if(selectionStart==null)return;var raw=e.GetPosition(previewHost);var point=new Point(Math.Clamp(raw.X,0,previewHost.ActualWidth),Math.Clamp(raw.Y,0,previewHost.ActualHeight));Canvas.SetLeft(selection,Math.Min(point.X,selectionStart.Value.X));Canvas.SetTop(selection,Math.Min(point.Y,selectionStart.Value.Y));selection.Width=Math.Abs(point.X-selectionStart.Value.X);selection.Height=Math.Abs(point.Y-selectionStart.Value.Y);
    }
    private void EndSelection(object sender,MouseButtonEventArgs e)
    {
        if(selectionStart==null)return;var first=SourcePoint(selectionStart.Value);var last=SourcePoint(e.GetPosition(previewHost));overlay.ReleaseMouseCapture();selectionStart=null;
        if(operation.Id is "redactAudio"or"trimAudio")
        {
            double start=Math.Clamp(Canvas.GetLeft(selection)/Math.Max(1,previewHost.ActualWidth)*totalDuration,0,totalDuration),end=Math.Clamp((Canvas.GetLeft(selection)+selection.Width)/Math.Max(1,previewHost.ActualWidth)*totalDuration,0,totalDuration);
            Set("start",start.ToString("0.000",CultureInfo.InvariantCulture));Set("end",end.ToString("0.000",CultureInfo.InvariantCulture));
            if(end-start<.001){selection.Visibility=Visibility.Collapsed;return;}if(operation.Id=="redactAudio"){var r=new Dictionary<string,double>{["start"]=start,["end"]=end};audioRanges.Add(r);var row=new ListBoxItem{Content=$"{start:0.000}–{end:0.000} 秒",Tag=r};regionList.Items.Add(row);regionList.SelectedItem=row;status.Text=$"已添加 {audioRanges.Count} 个蜂鸣时段。";}return;
        }
        int x=(int)Math.Min(first.X,last.X),y=(int)Math.Min(first.Y,last.Y),w=(int)Math.Abs(last.X-first.X),h=(int)Math.Abs(last.Y-first.Y);if(w<1||h<1)return;
        syncingRegion=true;Set("x",x.ToString());Set("y",y.ToString());Set("width",w.ToString());Set("height",h.ToString());syncingRegion=false;
        if(operation.Id is "redactImage"or"redactVideo")
        {
            double start=TimeValue(Get("start","0")),end=TimeValue(Get("end","0"));
            var region=new Dictionary<string,object>{["x"]=x,["y"]=y,["width"]=w,["height"]=h,["start"]=start,["end"]=end,["style"]=Get("style","pixelate"),["blockSize"]=Get("blockSize","12")};regions.Add(region);var row=new ListBoxItem{Content=$"区域 {regions.Count} · {w} × {h} · {Choice(Get("style"))}",Tag=region};regionList.Items.Add(row);regionList.SelectedItem=row;status.Text=$"已添加 {regions.Count} 个覆盖区域。";
        }SchedulePreview();
    }
    private static double TimeValue(string value)=>JobValidation.Time(value);
    private void SetMetadataState(){if(controls.TryGetValue("metadata",out var table)&&table is MetadataEditor editor)editor.IsReadOnly=Get("remove")=="true";}
    private void ParameterChanged()
    {
        if(syncingRegion)return;
        if(regionList.SelectedItem is ListBoxItem item&&item.Tag is Dictionary<string,object> r)
        {
            try{foreach(string key in new[]{"x","y","width","height","start","end","blockSize"})if(controls.ContainsKey(key))r[key]=TimeValue(Get(key,"0"));r["style"]=Get("style","pixelate");item.Content=$"区域 {regionList.SelectedIndex+1} · {r["width"]} × {r["height"]} · {Choice(Get("style"))}";}catch(ArgumentException){}catch(FormatException){}
        }
        if(operation.Id=="redactAudio"&&regionList.SelectedItem is ListBoxItem audioItem&&audioItem.Tag is Dictionary<string,double> range)
        {
            try{range["start"]=TimeValue(Get("start","0"));range["end"]=TimeValue(Get("end","0"));audioItem.Content=$"{range["start"]:0.000}–{range["end"]:0.000} 秒";}catch(ArgumentException){}
        }
        if(RefreshFrame)SchedulePreview();
    }
    private async Task Save()
    {
        if(!loaded||saving!=null)return;ConversionJob job;
        try{job=Capture();}catch(Exception ex){status.Text=ex.Message;status.Foreground=UiTheme.Accent;return;}
        saving=CancellationTokenSource.CreateLinkedTokenSource(closing.Token);saving.CancelAfter(TimeSpan.FromMinutes(30));previewDelay.Stop();previewCancellation?.Cancel();player.Stop();player.Source=null;playing=false;
        using var busy=activity.Begin("正在保存新文件",Path.GetFileName(paths[0]));
        bodyPanel.IsEnabled=false;saveButton.IsEnabled=false;revealButton.Visibility=Visibility.Collapsed;cancelButton.Visibility=Visibility.Visible;cancelButton.IsEnabled=true;status.Foreground=UiTheme.Muted;status.Text="处理期间可取消；原文件保留。";
        try
        {
            var result=await submit(job,saving.Token,busy);var success=result.Files.Where(f=>f.Output!=null).ToArray();var failures=result.Files.Where(f=>f.Error!=null).ToArray();
            if(success.Length>0){lastOutput=success[^1].Output;revealButton.Visibility=Visibility.Visible;}
            status.Text=failures.Length==0?$"已保存 {success.Length} 项：{Path.GetFileName(lastOutput)}":$"成功 {success.Length} 项，失败 {failures.Length} 项：{failures[0].Error}";
            Journal.Write("ToolSaved",new{job.Action,success=success.Length,failures=failures.Length});
        }
        catch(OperationCanceledException){status.Text="处理已取消，可调整参数后重新保存。";}
        catch(Exception ex){status.Text="保存未完成："+ex.Message;status.Foreground=UiTheme.Accent;}
        finally{saving.Dispose();saving=null;bodyPanel.IsEnabled=true;saveButton.IsEnabled=true;cancelButton.Visibility=Visibility.Collapsed;}
    }
    private async Task StepFrame(int direction)
    {
        if(Catalog.Category(paths[0])!="video"){timeline.Value=Math.Clamp(timeline.Value+direction*.1,0,totalDuration);return;}
        using var busy=activity.Begin("正在定位视频帧",Path.GetFileName(paths[0]));
        try{var job=Capture();job.Parameters!["direction"]=direction.ToString();job.Parameters["time"]=timeline.Value.ToString(CultureInfo.InvariantCulture);var info=await Backend.Preview(job,folder,closing.Token,"--frame-step");timeline.Value=info.GetProperty("Time").GetDouble();}catch(Exception ex){status.Text="逐帧："+ex.Message;}
    }
    private async Task Play(bool original)
    {
        if(playing&&original==playingOriginal){player.Stop();player.Visibility=Visibility.Collapsed;image.Visibility=Visibility.Visible;playing=false;if(playButton!=null)playButton.Content=PlaybackLabel;return;}
        using var busy=activity.Begin("正在准备播放",Path.GetFileName(paths[0]));
        try
        {
            player.Stop();player.Source=null;
            status.Text="准备当前时段的本地播放预览…";var job=original?new ConversionJob(paths,"preview-original",new(){["time"]=timeline.Value.ToString(CultureInfo.InvariantCulture)}):Capture();if(original)job=job with{Action="preview-original"};var info=await Backend.Preview(job,folder,closing.Token,"--playback");
            bool audio=Catalog.Category(paths[0])=="audio";player.Source=new Uri(info.GetProperty("Preview").GetString()!);player.Visibility=audio?Visibility.Hidden:Visibility.Visible;image.Visibility=audio?Visibility.Visible:Visibility.Collapsed;player.Play();playing=true;playingOriginal=original;if(playButton!=null)playButton.Content="停止播放";status.Text="播放当前时段（最多 30 秒）；导出处理完整选择。";
        }catch(OperationCanceledException){}catch(Exception ex){status.Text="播放："+ex.Message;}
    }
}
