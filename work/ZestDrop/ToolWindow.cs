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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace ZestDrop;

internal sealed class ToolWindow : Window
{
    private readonly string[] paths;
    private readonly Operation operation;
    private readonly Func<ConversionJob, CancellationToken, IProgress<JobProgress>?, Task<BatchResult>> submit;
    private readonly Dictionary<string, FrameworkElement> controls = [];
    private readonly Dictionary<string, TextBlock> fieldLabels = [];
    private readonly TextBlock status = new(), details = new();
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly Canvas overlay = new() { Background = Brushes.Transparent };
    private readonly Grid previewHost = new();
    private readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop };
    private readonly ListBox order = new() { Height = 120 };
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 1 };
    private readonly TextBlock positionLabel = new() { Foreground = UiTheme.Muted, Margin = new Thickness(0, 8, 0, 4) };
    private readonly Rectangle selection = new() { Stroke = Brushes.DarkOrange, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 255, 120, 20)), Visibility = Visibility.Collapsed };
    private readonly List<Dictionary<string, object>> regions = [];
    private readonly List<Dictionary<string, double>> audioRanges = [];
    private readonly ListBox regionList = new() { Height = 82 };
    private readonly CancellationTokenSource closing = new();
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private readonly DispatcherTimer previewDelay;
    private string folder = Path.Combine(Journal.DirectoryPath, "preview-" + Guid.NewGuid().ToString("N"));
    private string? originalPreview;
    private int originalWidth, originalHeight, pages;
    private double totalDuration, fps = 30;
    private Point? selectionStart;
    private bool loaded, showingOriginal = true;
    private bool rangeEdited;
    private int previewVersion;
    private string metadataText = "";
    private Button saveButton = null!, cancelButton = null!, revealButton = null!, copyButton = null!;
    private readonly BusyLine busyLine = new();
    private readonly CropFrame cropFrame = new();
    private string editedField = "";
    private FrameworkElement bodyPanel = null!;
    private CancellationTokenSource? saving, previewCancellation;
    private string? lastOutput;
    private readonly ComboBox framePicker = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    private bool syncingRegion;
    private int imageFrame = -1;
    private bool Cropping => operation.Id is "cropImage" or "cropVideo";
    // Rotating is previewed by transforming the picture (or the playing video) itself, so every change shows at once.
    private bool Rotating => operation.Id is "rotateImage" or "rotateVideo";
    internal bool DebugReady => loaded;
    private readonly Grid rotateStage = new();
    private readonly Border rotatePlate = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    // Cropping is previewed by the frame itself, so the preview is not regenerated while its settings change.
    private bool LivePreview => RefreshFrame && !HasMedia && operation.Id != "cropImage";
    private bool VisualPreview => operation.Id == "watermark" && Catalog.Category(paths[0]) == "image" || operation.Id is "editImage" or "frameImage" or "cropImage" or "cropVideo" or "redactImage" or "redactVideo" or "organizePDF" or "createCollage";
    private bool ProcessedPlayback => operation.Id is "normalizeAudio" or "audioChannels" or "audioToVideo" or "redactAudio" or "trimAudio" or "trimVideo" or "cropVideo" or "changeVideoSpeed" or "redactVideo" or "muteVideo";
    private bool HasMedia => Catalog.Category(paths[0]) is "audio" or "video";
    private MediaTransport? transport;
    private bool syncingPlayback;
    private bool syncingTrim;
    private bool HasTimeline => operation.Id is "videoToGif" or "cropVideo" or "redactVideo" or "videoSnapshots" or "trimVideo" or "trimAudio" or "redactAudio";
    private bool RefreshFrame => VisualPreview || HasTimeline && Catalog.Category(paths[0]) == "video";
    private Button? compareButton;

    public ToolWindow(string[] paths, Operation operation, Func<ConversionJob, CancellationToken, IProgress<JobProgress>?, Task<BatchResult>> submit)
    {
        this.paths = paths;
        this.operation = operation;
        this.submit = submit;
        Title = operation.Label + " · " + Path.GetFileName(paths[0]);
        Width = 1040;
        Height = 800;
        MinWidth = 850;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(248, 249, 251));
        FontFamily = UiTheme.Font;
        FontSize = 13;
        previewDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        previewDelay.Tick += async (_, _) => { previewDelay.Stop(); await UpdatePreview(); };
        UiTheme.Apply(this);
        Content = UiTheme.Frame(this, Build());
        Loaded += async (_, _) => await Initialize();
        Closing += (_, e) => { if (saving != null) { e.Cancel = true; status.Text = L.T("处理中。请先点击取消，等待任务停止后关闭窗口。"); } };
        PreviewKeyDown += async (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await Save(); } };
        Closed += async (_, _) =>
        {
            closing.Cancel();
            previewCancellation?.Cancel();
            previewDelay.Stop();
            transport?.Dispose();
            player.Stop();
            player.Source = null;
            await Task.Delay(1000);
            try
            {
                string root = Path.GetFullPath(Journal.DirectoryPath) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(folder).StartsWith("preview-") && Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };
    }
    private UIElement Build()
    {
        var root = new Grid { Margin = new Thickness(24, 8, 24, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = operation.Label, FontSize = 26, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = paths.Length == 1 ? Path.GetFileName(paths[0]) : (paths.Length == 1 ? L.T("已选 1 个文件") : L.F("已选 {0} 个文件", paths.Length)), Foreground = UiTheme.Muted, Margin = new Thickness(0, 6, 0, 20) });
        root.Children.Add(header);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        bodyPanel = body;
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var left = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        left.Children.Add(new TextBlock { Text = VisualPreview || Rotating ? L.T("效果预览") : L.T("源文件预览"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        left.Children.Add(framePicker);
        framePicker.SelectionChanged += async (_, _) => { if (loaded && framePicker.SelectedItem is ComboBoxItem item) { imageFrame = (int)item.Tag; regions.Clear(); regionList.Items.Clear(); await Initialize(); if (LivePreview) SchedulePreview(); } };
        previewHost.Height = 300;
        previewHost.Background = new SolidColorBrush(Color.FromRgb(244, 245, 247));
        if (Rotating)
        {
            rotateStage.Children.Add(rotatePlate);
            rotateStage.Children.Add(image);
            rotateStage.Children.Add(player);
            previewHost.Children.Add(rotateStage);
            previewHost.SizeChanged += (_, _) => UpdateRotationPreview();
        }
        else
        {
            previewHost.Children.Add(image);
            previewHost.Children.Add(player);
        }
        player.Visibility = Visibility.Collapsed;
        previewHost.Children.Add(overlay);
        overlay.Children.Add(selection);
        if (Cropping)
            AddCropFrame();
        // A thin line instead of a card: it never hides the picture and only appears if the work takes a moment.
        previewHost.Children.Add(busyLine);
        left.Children.Add(previewHost);
        overlay.MouseLeftButtonDown += BeginSelection;
        overlay.MouseMove += MoveSelection;
        overlay.MouseLeftButtonUp += EndSelection;
        var previewButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        if (VisualPreview && !HasMedia)
        {
            compareButton = Button(L.T("查看效果"), async () =>
            {
                if (showingOriginal)
                { await UpdatePreview(); }
                else if (originalPreview != null)
                { player.Stop(); player.Visibility = Visibility.Collapsed; SetImage(originalPreview); showingOriginal = true; compareButton!.Content = L.T("查看效果"); status.Text = L.T("正在查看原始画面。"); UpdateCropVisibility(); }
            });
            previewButtons.Children.Add(compareButton);
        }
        left.Children.Add(previewButtons);
        if (HasMedia)
        {
            previewHost.Background = Brushes.Black;
            transport = new MediaTransport(player, timeline, paths, Catalog.Category(paths[0]) == "audio", ProcessedPlayback && paths.Length == 1, PreparePlayback, closing.Token, text => status.Text = text);
            transport.ViewChanged += () => { player.Visibility = transport.HasVideo ? Visibility.Visible : Visibility.Hidden; image.Visibility = transport.HasVideo ? Visibility.Collapsed : Visibility.Visible; };
            left.Children.Remove(previewHost);
            var surface = new StackPanel { Background = Brushes.Black };
            surface.Children.Add(previewHost);
            surface.Children.Add(transport.Controls);
            var mediaSurface = new Border { Child = surface, CornerRadius = new CornerRadius(10), Background = Brushes.Black, ClipToBounds = true };
            mediaSurface.SizeChanged += (_, _) => mediaSurface.Clip = new RectangleGeometry(new Rect(0, 0, mediaSurface.ActualWidth, mediaSurface.ActualHeight), 10, 10);
            left.Children.Add(mediaSurface);
            left.Children.Add(transport);
            if (operation.Id is "trimVideo" or "trimAudio")
                transport.RangeChanged += (first, last) => { syncingTrim = true; Set("start", first.ToString("0.000", CultureInfo.InvariantCulture)); Set("end", last.ToString("0.000", CultureInfo.InvariantCulture)); syncingTrim = false; transport.InvalidateEffect(); };
        }
        if (HasTimeline || HasMedia)
        {
            if (!HasMedia)
            { left.Children.Add(positionLabel); left.Children.Add(timeline); }
            System.Windows.Automation.AutomationProperties.SetName(timeline, L.T("播放位置（秒）"));
            timeline.ValueChanged += (_, _) => { if (loaded) { syncingPlayback = transport?.IsSynchronizing == true; Set("time", timeline.Value.ToString("0.000", CultureInfo.InvariantCulture)); syncingPlayback = false; if (LivePreview) SchedulePreview(); } };
            var frameButtons = new WrapPanel();
            if (operation.Id == "videoSnapshots")
            { frameButtons.Children.Add(Button(L.T("上一帧"), () => StepFrame(-1))); frameButtons.Children.Add(Button(L.T("下一帧"), () => StepFrame(1))); }
            if ((operation.Fields ?? []).Any(f => f.Name == "start"))
            { frameButtons.Children.Add(Button(L.T("设为开始"), () => { Set("start", timeline.Value.ToString("0.000", CultureInfo.InvariantCulture)); return Task.CompletedTask; })); frameButtons.Children.Add(Button(L.T("设为结束"), () => { Set("end", timeline.Value.ToString("0.000", CultureInfo.InvariantCulture)); return Task.CompletedTask; })); }
            left.Children.Add(frameButtons);
            if (transport != null)
                transport.ModeChanged += () => { frameButtons.IsEnabled = !transport.EffectSelected; UpdateCropVisibility(); };
        }
        StackPanel? rotationPanel = null;
        if (Rotating)
        {
            rotationPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            left.Children.Add(rotationPanel);
            if (operation.Id == "rotateVideo")
                left.Children.Add(new TextBlock { Text = L.T("保留完整时长和原有音轨；视频会重新编码。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
        }
        if (operation.Id == "cropVideo")
            left.Children.Add(new TextBlock { Text = L.T("只裁剪画面；保留完整时长和原有音轨。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
        if (operation.Id is "cropImage" or "cropVideo" or "redactImage" or "redactVideo")
        {
            bool cropping = operation.Id is "cropImage" or "cropVideo";
            left.Children.Add(new TextBlock { Text = cropping ? L.T("拖动裁剪框移动位置，拖动边角或边缘调整大小。") : L.T("在预览上拖动选区，可添加多个打码区域。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
            left.Children.Add(Button(cropping ? L.T("重置裁剪") : L.T("清空覆盖区域"), () =>
            {
                if (cropping)
                { ResetCrop(); return Task.CompletedTask; }
                regions.Clear();
                regionList.Items.Clear();
                Set("regions", "");
                selection.Visibility = Visibility.Collapsed;
                Set("width", "0");
                Set("height", "0");
                rangeEdited = false;
                if (originalPreview != null)
                { SetImage(originalPreview); showingOriginal = true; }
                status.Text = L.T("覆盖区域已清空。请拖动添加新区域。");
                return Task.CompletedTask;
            }));
            if (operation.Id is "redactImage" or "redactVideo")
            {
                left.Children.Add(regionList);
                regionList.SelectionChanged += (_, _) =>
                {
                    if (regionList.SelectedItem is ListBoxItem item && item.Tag is Dictionary<string, object> r)
                    { syncingRegion = true; foreach (string key in new[] { "x", "y", "width", "height", "start", "end", "style", "blockSize" }) if (r.TryGetValue(key, out var value)) Set(key, value.ToString()!); syncingRegion = false; }
                };
                var regionButtons = new WrapPanel();
                regionButtons.Children.Add(Button(L.T("移除选中区域"), () => { if (regionList.SelectedItem is ListBoxItem item && item.Tag is Dictionary<string, object> r) { regions.Remove(r); regionList.Items.Remove(item); if (regions.Count == 0) { Set("width", "0"); Set("height", "0"); rangeEdited = false; if (originalPreview != null) { SetImage(originalPreview); showingOriginal = true; } } else SchedulePreview(); } return Task.CompletedTask; }));
                left.Children.Add(regionButtons);
            }
        }
        if (operation.Id == "redactAudio")
        {
            left.Children.Add(new TextBlock { Text = L.T("在波形上拖动选择时段；可添加多个蜂鸣范围。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
            left.Children.Add(Button(L.T("清空蜂鸣范围"), () => { audioRanges.Clear(); regionList.Items.Clear(); Set("ranges", ""); rangeEdited = false; selection.Visibility = Visibility.Collapsed; status.Text = L.T("蜂鸣时段已清空。"); return Task.CompletedTask; }));
        }
        if (operation.Id == "redactAudio")
        {
            left.Children.Add(regionList);
            left.Children.Add(Button(L.T("移除选中时段"), () => { if (regionList.SelectedItem is ListBoxItem item && item.Tag is Dictionary<string, double> r) { audioRanges.Remove(r); regionList.Items.Remove(item); rangeEdited = audioRanges.Count > 0; } return Task.CompletedTask; }));
            regionList.SelectionChanged += (_, _) => { if (regionList.SelectedItem is ListBoxItem item && item.Tag is Dictionary<string, double> r) { syncingRegion = true; Set("start", r["start"].ToString(CultureInfo.InvariantCulture)); Set("end", r["end"].ToString(CultureInfo.InvariantCulture)); syncingRegion = false; } };
        }
        if (operation.Id == "normalizeAudio")
            left.Children.Add(Button(L.T("分析原始与处理后响度"), async () =>
        {
            using var busy = busyLine.Begin();
            try
            { status.Text = L.T("分析响度…"); var info = await Backend.Preview(Capture(), folder, closing.Token, "--analyze"); status.Text = L.F("原始 {0} LUFS · 处理后 {1} LUFS", info.GetProperty("Input").GetString(), info.GetProperty("Output").GetString()); }
            catch (Exception ex) { status.Text = L.T("分析：") + ex.Message; }
        }));
        left.Children.Add(new ScrollViewer { Content = details, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        details.TextWrapping = TextWrapping.Wrap;
        details.Foreground = Brushes.DimGray;
        details.Margin = new Thickness(0, 8, 0, 8);
        if (operation.Ordered || operation.Id == "organizePDF")
        {
            left.Children.Add(order);
            var reorder = new WrapPanel();
            reorder.Children.Add(Button(L.T("上移"), () => { MoveItem(-1); return Task.CompletedTask; }));
            reorder.Children.Add(Button(L.T("下移"), () => { MoveItem(1); return Task.CompletedTask; }));
            reorder.Children.Add(Button(L.T("移出选择"), () => { if (order.SelectedIndex >= 0) order.Items.RemoveAt(order.SelectedIndex); return Task.CompletedTask; }));
            if (operation.Id == "organizePDF")
                reorder.Children.Add(Button(L.T("复制页"), () => { if (order.SelectedItem is ListBoxItem item) { int index = order.SelectedIndex; order.Items.Insert(index + 1, new ListBoxItem { Content = item.Content, Tag = item.Tag }); } return Task.CompletedTask; }));
            left.Children.Add(reorder);
            order.SelectionChanged += (_, _) => { if (loaded && VisualPreview) SchedulePreview(); };
        }
        var previewCard = UiTheme.Card(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(18));
        previewCard.Margin = new Thickness(0, 0, 16, 0);
        body.Children.Add(previewCard);
        var parameters = new StackPanel();
        parameters.Children.Add(new TextBlock { Text = L.T("处理参数"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var advanced = new StackPanel();
        foreach (var field in operation.Fields ?? [])
        {
            var fieldHost = field.Kind == "json" ? advanced : parameters;
            var fieldLabel = new TextBlock { Text = field.Label, Margin = new Thickness(0, 9, 0, 4), TextWrapping = TextWrapping.Wrap };
            if (Rotating && field.Name == "background" && Path.GetExtension(paths[0]).ToLowerInvariant() is ".jpg" or ".jpeg" or ".bmp")
                fieldLabel.Text = L.T("空白处颜色（留空为白色）");
            fieldLabels[field.Name] = fieldLabel;
            fieldHost.Children.Add(fieldLabel);
            FrameworkElement control;
            if (field.Name == "metadata")
                control = new MetadataEditor();
            else if (field.Kind == "bool")
                control = new CheckBox { IsChecked = field.Default == "true", Content = L.T("启用") };
            else if (field.Kind == "choice")
            {
                var combo = new ComboBox();
                foreach (string choice in field.Choices ?? [])
                    combo.Items.Add(new ComboBoxItem { Content = Choice(choice), Tag = choice });
                combo.SelectedIndex = Array.IndexOf(field.Choices ?? [], field.Default);
                control = combo;
            }
            else if (field.Kind == "password")
                control = new PasswordBox();
            else
            {
                var box = new TextBox { Text = field.Default, Padding = new Thickness(10, 8, 10, 8), AcceptsReturn = field.Kind == "json", TextWrapping = field.Kind == "json" ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                if (field.Kind == "json")
                { box.Height = 105; box.FontFamily = new FontFamily("Consolas"); }
                box.TextChanged += (_, _) => { if (loaded) { editedField = field.Name; if (new[] { "x", "y", "width", "height", "start", "end" }.Contains(field.Name)) rangeEdited = true; if (field.Name == "search") FilterMetadata(box.Text); else ParameterChanged(); } };
                // While a number is being typed the frame follows it; once the box is left, the box shows what the frame really is.
                if (Cropping && field.Name is "x" or "y" or "width" or "height")
                    box.LostKeyboardFocus += (_, _) => { if (loaded) WriteCropFields(); };
                control = box;
            }
            controls[field.Name] = control;
            fieldHost.Children.Add(control);
            System.Windows.Automation.AutomationProperties.SetName(control, field.Label);
            if (control is ComboBox choiceControl)
                choiceControl.SelectionChanged += (_, _) => { if (loaded && (VisualPreview || Rotating)) { editedField = field.Name; ParameterChanged(); } };
            if (control is CheckBox toggle && field.Name == "remove")
            { toggle.Checked += (_, _) => SetMetadataState(); toggle.Unchecked += (_, _) => SetMetadataState(); }
            if (field.Kind.StartsWith("file"))
            {
                var (pickLabel, pickFilter) = field.Kind switch
                {
                    "file:audio" => (L.T("选择音频…"), L.T("音频|*.mp3;*.m4a;*.wav;*.flac;*.ogg;*.opus;*.aiff;*.wma|所有文件|*.*")),
                    "file:subtitle" => (L.T("选择字幕…"), L.T("字幕|*.srt;*.ass;*.ssa;*.vtt|所有文件|*.*")),
                    _ => (L.T("选择图片…"), L.T("图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.heic;*.avif|所有文件|*.*"))
                };
                parameters.Children.Add(Button(pickLabel, () => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = pickFilter }; if (dialog.ShowDialog(this) == true) Set(field.Name, dialog.FileName); return Task.CompletedTask; }));
            }
        }
        void ShowField(string name, bool visible)
        {
            if (controls.TryGetValue(name, out var shown))
                shown.Visibility = fieldLabels[name].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        void WhenChanged(string name, Action update)
        {
            ((ComboBox)controls[name]).SelectionChanged += (_, _) => update();
            update();
        }
        if (operation.Id == "resizeImage")
            WhenChanged("mode", () => { string mode = Get("mode", "percent"); ShowField("percent", mode == "percent"); ShowField("edge", mode == "edge"); foreach (string name in new[] { "width", "height", "keepRatio" }) ShowField(name, mode == "size"); });
        if (operation.Id == "watermark")
            WhenChanged("layout", () => { bool tiled = Get("layout", "single") == "tiled"; ShowField("position", !tiled); ShowField("margin", !tiled); });
        if (operation.Id == "pdfPassword")
            WhenChanged("mode", () => ShowField("newPassword", Get("mode", "add") == "add"));
        if (operation.Id == "createAnimation")
            WhenChanged("format", () => ShowField("loop", Get("format", "gif") == "gif"));
        if (operation.Id == "compress" && controls.ContainsKey("targetKB"))
        {
            // Common upload limits; a click fills the size target in.
            var limits = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (int megabytes in new[] { 8, 25, 50, 100 })
                limits.Children.Add(Button(megabytes + " MB", () => { Set("targetKB", (megabytes * 1024).ToString()); return Task.CompletedTask; }));
            limits.Children.Add(Button(L.T("不限制"), () => { Set("targetKB", "0"); return Task.CompletedTask; }));
            parameters.Children.Insert(parameters.Children.IndexOf(controls["targetKB"]) + 1, limits);
        }
        if (Presets.Supported(operation.Id))
            AddPresetBar(parameters);
        if (Rotating)
        {
            var angleBox = (TextBox)controls["angle"];
            parameters.Children.Remove(angleBox); parameters.Children.Remove(fieldLabels["angle"]);
            angleBox.Width = 76; angleBox.HorizontalContentAlignment = HorizontalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(angleBox, RotationDial.Caption);
            var heading = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var value = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            value.Children.Add(angleBox); value.Children.Add(new TextBlock { Text = "°", Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = UiTheme.Muted });
            DockPanel.SetDock(value, Dock.Right); heading.Children.Add(value);
            Task Turn(int quarters)
            {
                _ = double.TryParse(Get("angle", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double angle);
                Set("angle", RotationDial.Signed(angle + 90 * quarters).ToString("0.#", CultureInfo.InvariantCulture)); return Task.CompletedTask;
            }
            var turnButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            turnButtons.Children.Add(Button(L.T("↺ 向左转 90°"), () => Turn(-1)));
            turnButtons.Children.Add(Button(L.T("↻ 向右转 90°"), () => Turn(1)));
            DockPanel.SetDock(turnButtons, Dock.Right); heading.Children.Add(turnButtons);
            heading.Children.Add(new TextBlock { Text = RotationDial.Caption, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Medium });
            rotationPanel!.Children.Add(heading);
            var dial = new RotationDial(); rotationPanel.Children.Add(dial);
            rotationPanel.Children.Add(new TextBlock { Text = RotationDial.Help, Foreground = UiTheme.Muted, FontSize = 10, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center });
            bool syncingAngle = false;
            void ShowCornerFields()
            {
                _ = double.TryParse(Get("angle", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double angle);
                var visible = angle % 90 != 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (string name in new[] { "expand", "background" })
                    controls[name].Visibility = fieldLabels[name].Visibility = name == "background" && Get("expand") == "crop" ? Visibility.Collapsed : visible;
            }
            dial.ValueChanged += (_, _) => { if (!syncingAngle) { syncingAngle = true; angleBox.Text = dial.Value.ToString("0.#", CultureInfo.InvariantCulture); syncingAngle = false; } };
            angleBox.TextChanged += (_, _) =>
            {
                if (!syncingAngle && double.TryParse(angleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed) && typed is >= -180 and <= 180)
                { syncingAngle = true; dial.Value = typed; syncingAngle = false; }
                ShowCornerFields();
            };
            angleBox.LostKeyboardFocus += (_, _) =>
            {
                if (!double.TryParse(angleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double typed) || !double.IsFinite(typed)) typed = dial.Value;
                angleBox.Text = Math.Clamp(Math.Round(typed, 1), -180, 180).ToString("0.#", CultureInfo.InvariantCulture);
            };
            ShowCornerFields();
            ((ComboBox)controls["expand"]).SelectionChanged += (_, _) => ShowCornerFields();
        }
        if (operation.Id is "redactImage" or "redactVideo")
        {
            void UpdateRedactionFields()
            { bool mosaic = Get("style") == "pixelate"; controls["blockSize"].Visibility = fieldLabels["blockSize"].Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed; if (controls.TryGetValue("color", out var color)) color.Visibility = fieldLabels["color"].Visibility = Get("style") == "solid" ? Visibility.Visible : Visibility.Collapsed; }
            ((ComboBox)controls["style"]).SelectionChanged += (_, _) => UpdateRedactionFields();
            UpdateRedactionFields();
        }
        if (controls.ContainsKey("ratio"))
        {
            var customRatio = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            customRatio.Children.Add(new TextBlock { Text = L.T("自定义比例（宽 : 高）"), Margin = new Thickness(0, 0, 0, 6) });
            var inputs = new Grid();
            inputs.ColumnDefinitions.Add(new ColumnDefinition());
            inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            inputs.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (string key in new[] { "ratioWidth", "ratioHeight" })
            { parameters.Children.Remove(controls[key]); parameters.Children.Remove(fieldLabels[key]); }
            inputs.Children.Add(controls["ratioWidth"]);
            Grid.SetColumn(controls["ratioHeight"], 2);
            inputs.Children.Add(controls["ratioHeight"]);
            var separator = new TextBlock { Text = ":", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(separator, 1);
            inputs.Children.Add(separator);
            customRatio.Children.Add(inputs);
            parameters.Children.Add(customRatio);
            void ShowCustomRatio() => customRatio.Visibility = Get("ratio") == "custom" ? Visibility.Visible : Visibility.Collapsed;
            ((ComboBox)controls["ratio"]).SelectionChanged += (_, _) => ShowCustomRatio();
            ShowCustomRatio();
        }
        if (advanced.Children.Count > 0)
            parameters.Children.Add(new Expander { Header = L.T("高级参数（可选）"), Content = advanced, Margin = new Thickness(0, 16, 0, 0) });
        if ((operation.Fields?.Length ?? 0) == 0)
            parameters.Children.Add(new TextBlock { Text = operation.Ordered ? L.T("在左侧调整文件顺序，然后保存。") : L.T("此操作无需参数。预览确认后保存新文件。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap });
        var scroll = UiTheme.Card(new ScrollViewer { Content = parameters, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new Thickness(18));
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        var bottom = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var feedback = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        status.Text = L.T("读取文件信息…");
        status.TextWrapping = TextWrapping.Wrap;
        status.Foreground = UiTheme.Muted;
        feedback.Children.Add(status);
        bottom.Children.Add(feedback);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(actions, 1);
        bottom.Children.Add(actions);
        revealButton = Button(L.T("打开输出位置"), () => { if (lastOutput != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + lastOutput + "\"") { UseShellExecute = true }); return Task.CompletedTask; });
        revealButton.Visibility = Visibility.Collapsed;
        actions.Children.Add(revealButton);
        copyButton = Button(L.T("复制结果"), () => { CopyResult(); return Task.CompletedTask; });
        copyButton.Visibility = Visibility.Collapsed;
        actions.Children.Add(copyButton);
        cancelButton = Button(L.T("取消处理"), () => { saving?.Cancel(); cancelButton.IsEnabled = false; status.Text = L.T("正在停止处理…"); return Task.CompletedTask; });
        cancelButton.Visibility = Visibility.Collapsed;
        actions.Children.Add(cancelButton);
        saveButton = Button(L.T("保存新文件"), Save);
        saveButton.IsEnabled = false;
        saveButton.Foreground = Brushes.White;
        saveButton.Background = UiTheme.Accent;
        saveButton.BorderThickness = new Thickness(0);
        saveButton.Padding = new Thickness(22, 10, 22, 10);
        saveButton.ToolTip = "Ctrl + Enter";
        actions.Children.Add(saveButton);
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        return root;
    }
    private static string Choice(string value) => value switch { "crop" => L.T("自动裁剪，去除空白边角"), "expand" => L.T("放大画布，保留整张画面"), "keep" => L.T("保持原尺寸（允许空白角）"), "none" => L.T("不翻转"), "horizontal" => L.T("左右翻转"), "vertical" => L.T("上下翻转"), "percent" => L.T("按比例"), "edge" => L.T("按最长边"), "size" => L.T("指定宽高"), "single" => L.T("单个"), "tiled" => L.T("平铺"), "original" => L.T("保持原样"), "add" => L.T("添加密码"), "remove" => L.T("移除密码"), "bottomRight" => L.T("右下"), "bottomCenter" => L.T("下中"), "bottomLeft" => L.T("左下"), "middleRight" => L.T("右中"), "center" => L.T("正中"), "middleLeft" => L.T("左中"), "topRight" => L.T("右上"), "topCenter" => L.T("上中"), "topLeft" => L.T("左上"), "solid" => L.T("纯色遮盖"), "blur" => L.T("模糊"), "pixelate" => L.T("马赛克"), "free" => L.T("自由"), "custom" => L.T("自定义"), "grid" => L.T("网格"), "row" => L.T("一行"), "column" => L.T("一列"), "featured" => L.T("主图布局"), "landscape" => L.T("横向"), "portrait" => L.T("纵向"), "square" => L.T("正方形"), "mono" => L.T("单声道"), "stereo" => L.T("双声道"), _ => value };
    private static Button Button(string text, Func<Task> click)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 6, 5) };
        button.Click += async (_, _) => await click();
        return button;
    }
    private async Task Initialize()
    {
        using var busy = busyLine.Begin();
        loaded = false;
        saveButton.IsEnabled = false;
        framePicker.IsEnabled = false;
        previewDelay.Stop();
        previewCancellation?.Cancel();
        previewVersion++;
        rangeEdited = false;
        if (operation.Ordered && order.Items.Count == 0)
            foreach (string path in paths)
                order.Items.Add(new ListBoxItem { Content = Path.GetFileName(path), Tag = path });
        try
        {
            var info = await Backend.Inspect(paths[0], folder, closing.Token, imageFrame);
            originalWidth = info.GetProperty("Width").GetInt32();
            originalHeight = info.GetProperty("Height").GetInt32();
            totalDuration = info.GetProperty("Duration").GetDouble();
            pages = info.GetProperty("Pages").GetInt32();
            double rate = info.GetProperty("FrameRate").GetDouble();
            if (rate > 0)
                fps = rate;
            originalPreview = info.GetProperty("Preview").GetString();
            if (originalPreview != null)
            { SetImage(originalPreview); showingOriginal = true; }
            if (paths.Length == 1 && info.TryGetProperty("Frames", out var frames) && frames.GetInt32() > 1)
            {
                if (framePicker.Items.Count == 0)
                { for (int i = 0; i < frames.GetInt32(); i++) framePicker.Items.Add(new ComboBoxItem { Content = L.F("第 {0} 帧", i + 1), Tag = i }); framePicker.SelectedIndex = info.GetProperty("Frame").GetInt32(); }
                framePicker.Visibility = Visibility.Visible;
                imageFrame = info.GetProperty("Frame").GetInt32();
            }
            if (originalWidth > 0 && operation.Id != "resizeImage")
            {
                Set("x", "0");
                Set("y", "0");
                Set("width", originalWidth.ToString());
                Set("height", originalHeight.ToString());
                details.Text = L.F("{0} × {1} 像素", originalWidth, originalHeight);
                if (Cropping)
                    cropFrame.SetSource(originalWidth, originalHeight);
            }
            if (totalDuration > 0)
            { timeline.Maximum = Math.Max(.001, totalDuration - .001); transport?.SetDuration(totalDuration); Set("end", totalDuration.ToString("0.000", CultureInfo.InvariantCulture)); details.Text += L.F("  · {0:0.00} 秒", totalDuration); if (Catalog.Category(paths[0]) == "video") details.Text += $" · {fps:0.##} fps"; positionLabel.Text = L.F("预览位置  {0:0.000} / {1:0.000} 秒", timeline.Value, totalDuration); }
            metadataText = JsonSerializer.Serialize(info.GetProperty("Metadata"), new JsonSerializerOptions { WriteIndented = true });
            Set("metadata", metadataText);
            if (controls.TryGetValue("metadata", out var editor) && editor is MetadataEditor table)
                table.Populate(info.GetProperty("Metadata"));
            if (operation.Id == "removeMetadata")
                details.Text = L.T("可搜索字段；关闭“移除全部”后可编辑元数据。");
            if (operation.Id == "organizePDF")
            {
                for (int i = 1; i <= pages; i++)
                    order.Items.Add(new ListBoxItem { Content = L.F("第 {0} 页", i), Tag = i });
                Set("pageOrder", string.Join(",", Enumerable.Range(1, pages)));
                details.Text = L.F("共 {0} 页；调整列表或输入页码顺序。", pages);
            }
            status.Text = imageFrame >= 0 ? L.T("此图片包含多个图像帧。仅导出当前帧，原始文件保留。") : L.T("调整参数后保存；新文件保存在原目录。");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = L.T("预览不可用：") + ex.Message + L.T("。仍可填写参数。"); }
        if (Presets.Supported(operation.Id) && Presets.Last(operation.Id) is { } remembered)
            ApplyValues(remembered);
        loaded = true;
        UpdateRotationPreview();
        saveButton.IsEnabled = true;
        framePicker.IsEnabled = true;
        SetMetadataState();
        SyncTrimRange();
        UpdateCropVisibility();
    }
    private void Set(string name, string value)
    {
        if (!controls.TryGetValue(name, out var control))
            return;
        if (control is TextBox text)
            text.Text = value;
        if (control is CheckBox check)
            check.IsChecked = value == "true";
        if (control is ComboBox combo)
            foreach (ComboBoxItem item in combo.Items)
                if (item.Tag?.ToString() == value)
                    combo.SelectedItem = item;
    }
    private string Get(string name, string fallback = "") => controls.TryGetValue(name, out var control) ? control switch { MetadataEditor table => table.ToJson(), TextBox text => text.Text, CheckBox check => check.IsChecked == true ? "true" : "false", ComboBox combo => ((ComboBoxItem?)combo.SelectedItem)?.Tag?.ToString() ?? fallback, PasswordBox pass => pass.Password, _ => fallback } : fallback;
    private ConversionJob Capture()
    {
        if (Cropping && cropFrame.HasSource)
        {
            // The frame is the truth: typed values that disagree with it (out of bounds, wrong ratio) are replaced by it.
            cropFrame.Ratio = CurrentRatio();
            WriteCropFields();
        }
        var values = new Dictionary<string, string>();
        foreach (var field in operation.Fields ?? [])
            values[field.Name] = Get(field.Name, field.Default);
        if (Rotating && double.TryParse(values["angle"], NumberStyles.Float, CultureInfo.InvariantCulture, out double rotation))
        {
            if (!double.IsFinite(rotation) || rotation is < -180 or > 180)
                throw new ArgumentException(L.English ? "Rotation must be between −180° and +180°." : "旋转角度需在 −180° 到 +180° 之间。");
            values["angle"] = RotationDial.BackendAngle(rotation).ToString("0.#", CultureInfo.InvariantCulture);
        }
        if (values.TryGetValue("ratio", out var selectedRatio) && selectedRatio != "custom")
        { values["ratioWidth"] = "3"; values["ratioHeight"] = "2"; }
        if (values.TryGetValue("style", out var selectedStyle) && selectedStyle != "pixelate")
            values["blockSize"] = "12";
        JobValidation.ResolveRatio(values);
        foreach (var field in operation.Fields ?? [])
            if (field.Kind == "number" && !string.IsNullOrWhiteSpace(values[field.Name]) && !values[field.Name].Contains(':') && !double.TryParse(values[field.Name], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw new ArgumentException(field.Label + L.T("需要数字"));
        values["fps"] = fps.ToString(CultureInfo.InvariantCulture);
        if (!values.ContainsKey("time"))
            values["time"] = timeline.Value.ToString(CultureInfo.InvariantCulture);
        if (regions.Count > 0)
            values["regions"] = JsonSerializer.Serialize(regions);
        if (audioRanges.Count > 0)
            values["ranges"] = JsonSerializer.Serialize(audioRanges);
        if (operation.Id is "redactImage" or "redactVideo" && regions.Count == 0 && string.IsNullOrWhiteSpace(Get("regions")) && !rangeEdited)
            throw new ArgumentException(L.T("请先在预览上选择覆盖区域。"));
        if (operation.Id == "redactAudio" && audioRanges.Count == 0 && string.IsNullOrWhiteSpace(Get("ranges")) && !rangeEdited)
            throw new ArgumentException(L.T("请先在波形上选择蜂鸣时段。"));
        string[] ordered = operation.Ordered ? order.Items.Cast<ListBoxItem>().Select(x => (string)x.Tag).ToArray() : paths;
        if (ordered.Length == 0)
            throw new ArgumentException(L.T("至少需要选择一个文件"));
        if (operation.Id == "organizePDF")
        {
            string generated = string.Join(",", order.Items.Cast<ListBoxItem>().Select(x => x.Tag));
            string initial = string.Join(",", Enumerable.Range(1, pages));
            if (order.Items.Count == 0 && values["pageOrder"] == initial)
                throw new ArgumentException(L.T("至少需要保留一页。"));
            if (values["pageOrder"] == initial)
                values["pageOrder"] = generated;
            if (order.SelectedItem is ListBoxItem selected)
                values["pageNumber"] = selected.Tag.ToString()!;
        }
        if (imageFrame >= 0)
            values["imageFrame"] = imageFrame.ToString();
        var job = new ConversionJob(ordered, operation.Id, values);
        JobValidation.Check(job, originalWidth, originalHeight, totalDuration, pages);
        return job;
    }
    private void SchedulePreview() { previewDelay.Stop(); previewDelay.Start(); }
    private async Task UpdatePreview()
    {
        if (!loaded || closing.IsCancellationRequested || saving != null || selectionStart != null)
            return;
        int version = ++previewVersion;
        previewCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        previewCancellation = cancellation;
        status.Text = L.T("更新效果中…");
        using var busy = busyLine.Begin();
        try
        {
            await previewGate.WaitAsync(cancellation.Token);
            try
            {
                var info = await Backend.Preview(Capture(), folder, cancellation.Token);
                if (version != previewVersion || cropFrame.IsDragging)
                    return;
                if (info.TryGetProperty("SourcePreview", out var source))
                    originalPreview = source.GetString();
                var preview = info.GetProperty("Preview").GetString();
                if (preview != null)
                { player.Visibility = Visibility.Collapsed; player.Stop(); SetImage(preview); selection.Visibility = Visibility.Collapsed; showingOriginal = false; if (compareButton != null) compareButton.Content = L.T("查看原始"); status.Text = L.T("预览已更新；保存后生成新文件。"); UpdateCropVisibility(); }
            }
            finally { previewGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == previewVersion) status.Text = L.T("预览：") + ex.Message; }
        finally { cancellation.Dispose(); if (ReferenceEquals(previewCancellation, cancellation)) previewCancellation = null; }
    }
    private void UpdateRotationPreview()
    {
        double width = previewHost.ActualWidth, height = previewHost.ActualHeight;
        if (!Rotating || originalWidth == 0 || width <= 0 || height <= 0)
            return;
        if (!double.TryParse(Get("angle", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double angle) || !double.IsFinite(angle)) return;
        angle = Math.Clamp(angle, -180, 180);
        string flip = Get("flip", "none");
        string canvas = Get("expand", HasMedia ? "expand" : "crop");
        bool quarter = angle % 90 == 0, keep = !quarter && canvas == "keep", cropEmpty = !quarter && !HasMedia && canvas == "crop";
        // The picture is shown fitted to the preview; the rotated picture (or, when the size is kept, the original frame) is then fitted again.
        double fit = Math.Min(width / originalWidth, height / originalHeight), shownWidth = originalWidth * fit, shownHeight = originalHeight * fit;
        double radians = angle * Math.PI / 180, cos = Math.Abs(Math.Cos(radians)), sin = Math.Abs(Math.Sin(radians));
        var cropped = cropEmpty ? RotationGeometry.CropSize(originalWidth, originalHeight, angle) : (Width: originalWidth, Height: originalHeight);
        double boxWidth = cropEmpty ? cropped.Width * fit : keep ? shownWidth : shownWidth * cos + shownHeight * sin;
        double boxHeight = cropEmpty ? cropped.Height * fit : keep ? shownHeight : shownWidth * sin + shownHeight * cos;
        double zoom = keep ? 1 : Math.Min(width / boxWidth, height / boxHeight);
        var turn = new TransformGroup();
        turn.Children.Add(new RotateTransform(angle));
        turn.Children.Add(new ScaleTransform(flip == "horizontal" ? -1 : 1, flip == "vertical" ? -1 : 1));
        turn.Children.Add(new ScaleTransform(zoom, zoom));
        foreach (FrameworkElement element in new FrameworkElement[] { image, player })
        { element.RenderTransformOrigin = new Point(.5, .5); element.RenderTransform = turn; }
        Color fill = Color.FromRgb(228, 231, 236);   // empty corners of a picture without a fill colour: transparent in the file
        if (Path.GetExtension(paths[0]).ToLowerInvariant() is ".jpg" or ".jpeg" or ".bmp") fill = Colors.White;
        try { if (!string.IsNullOrWhiteSpace(Get("background")) && ColorConverter.ConvertFromString(Get("background").Trim()) is Color chosen) fill = chosen; }
        catch (FormatException) { }
        rotatePlate.Visibility = quarter || cropEmpty ? Visibility.Collapsed : Visibility.Visible;
        rotatePlate.Width = boxWidth * zoom;
        rotatePlate.Height = boxHeight * zoom;
        rotatePlate.Background = new SolidColorBrush(fill);
        // Keeping the original size cuts off whatever leaves the original frame.
        rotateStage.Clip = cropEmpty ? new RectangleGeometry(new Rect((width - boxWidth * zoom) / 2, (height - boxHeight * zoom) / 2, boxWidth * zoom, boxHeight * zoom))
            : keep ? new RectangleGeometry(new Rect((width - shownWidth) / 2, (height - shownHeight) / 2, shownWidth, shownHeight)) : null;
        if (!HasMedia)
        {
            int outWidth = cropEmpty ? cropped.Width : keep ? originalWidth : (int)Math.Round(originalWidth * cos + originalHeight * sin);
            int outHeight = cropEmpty ? cropped.Height : keep ? originalHeight : (int)Math.Round(originalWidth * sin + originalHeight * cos);
            details.Text = L.F("{0} × {1} 像素", originalWidth, originalHeight) + (outWidth == originalWidth && outHeight == originalHeight ? "" : L.F(" → {0} × {1} 像素", outWidth, outHeight));
        }
    }
    private void ApplyValues(Dictionary<string, string> values)
    {
        foreach (var (name, value) in values)
            if (Presets.Keeps(name))
                Set(name, value);
    }
    private Dictionary<string, string> PresetValues() => (operation.Fields ?? []).Where(f => Presets.Keeps(f.Name) && f.Kind != "password").ToDictionary(f => f.Name, f => Get(f.Name, f.Default));
    private void AddPresetBar(Panel parameters)
    {
        var names = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) };
        void Reload()
        {
            names.Items.Clear();
            names.Items.Add(new ComboBoxItem { Content = L.T("预设…"), Tag = "" });
            foreach (string name in Presets.Names(operation.Id))
                names.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            names.SelectedIndex = 0;
        }
        names.SelectionChanged += (_, _) =>
        {
            if (names.SelectedItem is ComboBoxItem { Tag: string name } && name.Length > 0 && Presets.Named(operation.Id, name) is { } values)
            { ApplyValues(values); ParameterChanged(); status.Text = L.F("已应用预设“{0}”。", name); }
        };
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(names);
        row.Children.Add(Button(L.T("保存为预设"), () =>
        {
            string? name = PromptName();
            if (!string.IsNullOrWhiteSpace(name))
            { Presets.SaveNamed(operation.Id, name.Trim(), PresetValues()); Reload(); status.Text = L.F("已保存预设“{0}”。", name.Trim()); }
            return Task.CompletedTask;
        }));
        row.Children.Add(Button(L.T("删除预设"), () =>
        {
            if (names.SelectedItem is ComboBoxItem { Tag: string name } && name.Length > 0)
            { Presets.Delete(operation.Id, name); Reload(); }
            return Task.CompletedTask;
        }));
        Reload();
        parameters.Children.Insert(Math.Min(1, parameters.Children.Count), row);
    }
    private string? PromptName()
    {
        var box = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 12) };
        var dialog = new Window { Title = L.T("预设名称"), Owner = this, Width = 340, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Background, FontFamily = FontFamily };
        var ok = new Button { Content = L.T("保存"), IsDefault = true, Padding = new Thickness(18, 7, 18, 7), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => dialog.DialogResult = true;
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = L.T("给这组设置起个名字："), Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(box);
        stack.Children.Add(ok);
        dialog.Content = stack;
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true ? box.Text : null;
    }
    private void CopyResult()
    {
        if (lastOutput == null || !File.Exists(lastOutput))
            return;
        try
        {
            string extension = Path.GetExtension(lastOutput).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff" or ".ico")
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(lastOutput);
                bitmap.EndInit();
                Clipboard.SetImage(bitmap);
            }
            else if (extension == ".txt")
                Clipboard.SetText(File.ReadAllText(lastOutput));
            else
                Clipboard.SetFileDropList([lastOutput]);
            status.Text = L.T("已复制到剪贴板。");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException or NotSupportedException) { status.Text = L.T("复制失败：") + ex.Message; }
    }
    private void SetImage(string path)
    {
        using var stream = File.OpenRead(path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        image.Source = bitmap;
        image.Visibility = Visibility.Visible;
    }
    private void FilterMetadata(string search)
    {
        if (controls.TryGetValue("metadata", out var editor) && editor is MetadataEditor table)
            table.Filter(search);
        details.Text = L.T("可搜索字段；关闭“移除全部”后可编辑元数据。");
    }
    private void MoveItem(int direction)
    {
        int index = order.SelectedIndex, next = index + direction;
        if (index < 0 || next < 0 || next >= order.Items.Count)
            return;
        var item = order.Items[index];
        order.Items.RemoveAt(index);
        order.Items.Insert(next, item);
        order.SelectedIndex = next;
    }
    private void AddCropFrame()
    {
        if (operation.Id == "cropImage")
            previewHost.Height = 350;
        previewHost.Children.Add(cropFrame);
        cropFrame.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(cropFrame, L.T("裁剪选区"));
        cropFrame.DragStarted += () =>
        {
            // Grabbing the frame means the result preview is out of date, and a request still in flight is of no use.
            cropFrame.Ratio = CurrentRatio();
            previewDelay.Stop();
            previewCancellation?.Cancel();
            previewVersion++;
            transport?.Pause();
            if (status.Text == L.T("更新效果中…"))
                status.Text = L.T("调整参数后保存；新文件保存在原目录。");
        };
        cropFrame.Changed += () => WriteCropFields();
        cropFrame.DragEnded += () => { WriteCropFields(); transport?.InvalidateEffect(); };
    }
    // The frame is shown over the original picture; the cropped result and the video's "result" view replace it.
    private void UpdateCropVisibility() =>
        cropFrame.Visibility = Cropping && loaded && cropFrame.HasSource && showingOriginal && transport?.EffectSelected != true ? Visibility.Visible : Visibility.Collapsed;
    private CropRatio? CurrentRatio()
    {
        string ratio = Get("ratio", "free");
        return CropRatio.Parse(ratio == "custom" ? Get("ratioWidth") + ":" + Get("ratioHeight") : ratio);
    }
    private bool TryReadCropFields(out CropRect rect)
    {
        rect = default;
        var numbers = new double[4];
        string[] names = ["x", "y", "width", "height"];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(Get(names[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) || !double.IsFinite(numbers[i]) || Math.Abs(numbers[i]) > int.MaxValue)
                return false;
        rect = new CropRect((int)numbers[0], (int)numbers[1], (int)numbers[2], (int)numbers[3]);
        return true;
    }
    // Typed values move the frame. Numbers that are still being typed (too small, not a number yet) are left alone.
    private void FieldsToCropFrame()
    {
        if (!cropFrame.HasSource || !TryReadCropFields(out var typed))
            return;
        var ratio = CurrentRatio();
        cropFrame.Ratio = ratio;
        int min = cropFrame.MinSize;
        if (typed.Width < min || typed.Height < min)
            return;
        var keep = ratio == null ? CropKeep.Nothing : editedField == "width" ? CropKeep.Width : editedField == "height" ? CropKeep.Height : CropKeep.Nothing;
        var next = CropMath.Constrain(typed, originalWidth, originalHeight, ratio, min, keep);
        if (next != cropFrame.Crop)
            cropFrame.SetCrop(next);
        // The box being typed in is left alone so the cursor does not jump; the others follow the frame.
        WriteCropFields(editedField);
        if (!showingOriginal && originalPreview != null)
            ShowOriginalWithFrame();
    }
    private void WriteCropFields(string? except = null)
    {
        if (!cropFrame.HasSource)
            return;
        var crop = cropFrame.Crop;
        bool was = syncingRegion;
        syncingRegion = true;
        try
        {
            if (except != "x") Set("x", crop.X.ToString(CultureInfo.InvariantCulture));
            if (except != "y") Set("y", crop.Y.ToString(CultureInfo.InvariantCulture));
            if (except != "width") Set("width", crop.Width.ToString(CultureInfo.InvariantCulture));
            if (except != "height") Set("height", crop.Height.ToString(CultureInfo.InvariantCulture));
        }
        finally { syncingRegion = was; }
    }
    private void ShowOriginalWithFrame()
    {
        if (originalPreview != null)
        { player.Stop(); player.Visibility = Visibility.Collapsed; SetImage(originalPreview); }
        showingOriginal = true;
        if (compareButton != null)
            compareButton.Content = L.T("查看效果");
        UpdateCropVisibility();
    }
    private void ResetCrop()
    {
        if (!cropFrame.HasSource)
            return;
        bool was = syncingRegion;
        syncingRegion = true;
        try { Set("ratio", "free"); }
        finally { syncingRegion = was; }
        cropFrame.Ratio = null;
        cropFrame.SetCrop(new CropRect(0, 0, originalWidth, originalHeight));
        WriteCropFields();
        previewDelay.Stop();
        ShowOriginalWithFrame();
        transport?.InvalidateEffect();
    }
    // For screenshots and manual checks: puts the window into a known state (see the --debug-tool-render mode).
    internal void DebugScene(string scene)
    {
        foreach (string part in scene.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "busy")
            { busyLine.ShowDelay = TimeSpan.Zero; busyLine.Begin(); }
            else if (part.StartsWith("angle=") && Rotating)
                Set("angle", part[6..]);
            else if (part.StartsWith("ratio=") && controls.ContainsKey("ratio"))
            { Set("ratio", part[6..]); editedField = "ratio"; ParameterChanged(); }
            else if (part.StartsWith("crop=") && cropFrame.HasSource)
            {
                int[] v = part[5..].Split(',').Select(int.Parse).ToArray();
                cropFrame.SetCrop(CropMath.Constrain(new CropRect(v[0], v[1], v[2], v[3]), originalWidth, originalHeight, CurrentRatio(), cropFrame.MinSize));
                WriteCropFields();
            }
        }
    }
    private Point SourcePoint(Point point)
    {
        double scale = Math.Min(previewHost.ActualWidth / Math.Max(1, originalWidth), previewHost.ActualHeight / Math.Max(1, originalHeight));
        double offsetX = (previewHost.ActualWidth - originalWidth * scale) / 2, offsetY = (previewHost.ActualHeight - originalHeight * scale) / 2;
        return new Point(Math.Clamp((point.X - offsetX) / scale, 0, originalWidth), Math.Clamp((point.Y - offsetY) / scale, 0, originalHeight));
    }
    private void BeginSelection(object sender, MouseButtonEventArgs e)
    {
        bool audio = operation.Id is "redactAudio" or "trimAudio";
        if (!loaded || saving != null)
            return;
        if (!audio && (operation.Id is not ("redactImage" or "redactVideo") || originalWidth == 0))
            return;
        if (HasMedia)
        { if (transport?.EffectSelected == true) { status.Text = L.T("请切换到原文件，再选择画面或音频范围。"); return; } transport?.Pause(); }
        if (!showingOriginal && !HasMedia && originalPreview != null)
        { SetImage(originalPreview); showingOriginal = true; }
        previewDelay.Stop();
        previewCancellation?.Cancel();
        selectionStart = e.GetPosition(previewHost);
        overlay.CaptureMouse();
        selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(selection, selectionStart.Value.X);
        Canvas.SetTop(selection, selectionStart.Value.Y);
        selection.Width = selection.Height = 0;
    }
    private void MoveSelection(object sender, MouseEventArgs e)
    {
        if (selectionStart == null)
            return;
        var raw = e.GetPosition(previewHost);
        var point = new Point(Math.Clamp(raw.X, 0, previewHost.ActualWidth), Math.Clamp(raw.Y, 0, previewHost.ActualHeight));
        Canvas.SetLeft(selection, Math.Min(point.X, selectionStart.Value.X));
        Canvas.SetTop(selection, Math.Min(point.Y, selectionStart.Value.Y));
        selection.Width = Math.Abs(point.X - selectionStart.Value.X);
        selection.Height = Math.Abs(point.Y - selectionStart.Value.Y);
    }
    private void EndSelection(object sender, MouseButtonEventArgs e)
    {
        if (selectionStart == null)
            return;
        var first = SourcePoint(selectionStart.Value);
        var last = SourcePoint(e.GetPosition(previewHost));
        overlay.ReleaseMouseCapture();
        selectionStart = null;
        if (operation.Id is "redactAudio" or "trimAudio")
        {
            double start = Math.Clamp(Canvas.GetLeft(selection) / Math.Max(1, previewHost.ActualWidth) * totalDuration, 0, totalDuration), end = Math.Clamp((Canvas.GetLeft(selection) + selection.Width) / Math.Max(1, previewHost.ActualWidth) * totalDuration, 0, totalDuration);
            Set("start", start.ToString("0.000", CultureInfo.InvariantCulture));
            Set("end", end.ToString("0.000", CultureInfo.InvariantCulture));
            if (end - start < .001)
            { selection.Visibility = Visibility.Collapsed; return; }
            if (operation.Id == "redactAudio")
            { var r = new Dictionary<string, double> { ["start"] = start, ["end"] = end }; audioRanges.Add(r); var row = new ListBoxItem { Content = L.F("{0:0.000}–{1:0.000} 秒", start, end), Tag = r }; regionList.Items.Add(row); regionList.SelectedItem = row; status.Text = L.F("已添加 {0} 个蜂鸣时段。", audioRanges.Count); }
            return;
        }
        int x = (int)Math.Min(first.X, last.X), y = (int)Math.Min(first.Y, last.Y), w = (int)Math.Abs(last.X - first.X), h = (int)Math.Abs(last.Y - first.Y);
        if (w < 1 || h < 1)
            return;
        syncingRegion = true;
        Set("x", x.ToString());
        Set("y", y.ToString());
        Set("width", w.ToString());
        Set("height", h.ToString());
        syncingRegion = false;
        if (operation.Id is "redactImage" or "redactVideo")
        {
            double start = TimeValue(Get("start", "0")), end = TimeValue(Get("end", "0"));
            var region = new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h, ["start"] = start, ["end"] = end, ["style"] = Get("style", "pixelate"), ["blockSize"] = Get("blockSize", "12") };
            regions.Add(region);
            var row = new ListBoxItem { Content = L.F("区域 {0} · {1} × {2} · {3}", regions.Count, w, h, Choice(Get("style"))), Tag = region };
            regionList.Items.Add(row);
            regionList.SelectedItem = row;
            status.Text = L.F("已添加 {0} 个覆盖区域。", regions.Count);
        }
        if (!HasMedia)
            SchedulePreview();
    }
    private static double TimeValue(string value) => JobValidation.Time(value);
    private void SetMetadataState() { if (controls.TryGetValue("metadata", out var table) && table is MetadataEditor editor) editor.IsReadOnly = Get("remove") == "true"; }
    private void ParameterChanged()
    {
        if (syncingRegion || syncingPlayback || syncingTrim)
            return;
        SyncTrimRange();
        transport?.InvalidateEffect();
        if (Cropping)
            FieldsToCropFrame();
        if (Rotating)
            UpdateRotationPreview();
        if (regionList.SelectedItem is ListBoxItem item && item.Tag is Dictionary<string, object> r)
        {
            try
            { foreach (string key in new[] { "x", "y", "width", "height", "start", "end", "blockSize" }) if (controls.ContainsKey(key)) r[key] = TimeValue(Get(key, "0")); r["style"] = Get("style", "pixelate"); item.Content = L.F("区域 {0} · {1} × {2} · {3}", regionList.SelectedIndex + 1, r["width"], r["height"], Choice(Get("style"))); }
            catch (ArgumentException) { }
            catch (FormatException) { }
        }
        if (operation.Id == "redactAudio" && regionList.SelectedItem is ListBoxItem audioItem && audioItem.Tag is Dictionary<string, double> range)
        {
            try
            { range["start"] = TimeValue(Get("start", "0")); range["end"] = TimeValue(Get("end", "0")); audioItem.Content = L.F("{0:0.000}–{1:0.000} 秒", range["start"], range["end"]); }
            catch (ArgumentException) { }
        }
        if (LivePreview)
            SchedulePreview();
    }
    private void SyncTrimRange()
    {
        if (transport == null || operation.Id is not ("trimVideo" or "trimAudio") || totalDuration <= 0)
            return;
        try
        { double first = TimeValue(Get("start", "0")), last = TimeValue(Get("end", "0")); if (last == 0) last = totalDuration; if (first >= 0 && last > first && last <= totalDuration + .001) transport.SetTrimRange(first, last); }
        catch (ArgumentException) { }
        catch (FormatException) { }
    }
    private async Task Save()
    {
        if (!loaded || saving != null)
            return;
        ConversionJob job;
        try
        { job = Capture(); }
        catch (Exception ex) { status.Text = ex.Message; status.Foreground = UiTheme.Accent; return; }
        if (operation.Id is "rotateImage" or "rotateVideo" && double.TryParse(Get("angle", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double chosenAngle) && chosenAngle % 360 == 0 && Get("flip", "none") == "none")
        { status.Text = L.T("请选择旋转角度或翻转方式。"); status.Foreground = UiTheme.Accent; return; }
        saving = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        previewDelay.Stop();
        previewCancellation?.Cancel();
        transport?.Stop();
        player.Stop();
        player.Source = null;
        using var busy = busyLine.Begin();
        bodyPanel.IsEnabled = false;
        saveButton.IsEnabled = false;
        revealButton.Visibility = Visibility.Collapsed;
        copyButton.Visibility = Visibility.Collapsed;
        cancelButton.Visibility = Visibility.Visible;
        cancelButton.IsEnabled = true;
        status.Foreground = UiTheme.Muted;
        status.Text = L.T("处理期间可取消；原文件保留。");
        try
        {
            var result = await submit(job, saving.Token, busy);
            var success = result.Files.Where(f => f.Output != null).ToArray();
            var failures = result.Files.Where(f => f.Error != null).ToArray();
            if (success.Length > 0)
            { lastOutput = success[^1].Output; revealButton.Visibility = Visibility.Visible; copyButton.Visibility = Visibility.Visible; if (Presets.Supported(operation.Id)) Presets.SaveLast(operation.Id, PresetValues()); }
            status.Text = failures.Length == 0 ? L.F("已保存 {0} 项：{1}", success.Length, Path.GetFileName(lastOutput)) : L.F("成功 {0} 项，失败 {1} 项：{2}", success.Length, failures.Length, failures[0].Error);
            Journal.Write("ToolSaved", new { job.Action, success = success.Length, failures = failures.Length });
        }
        catch (OperationCanceledException) { status.Text = L.T("处理已取消，可调整参数后重新保存。"); }
        catch (Exception ex) { status.Text = L.T("保存未完成：") + ex.Message; status.Foreground = UiTheme.Accent; }
        finally { saving.Dispose(); saving = null; bodyPanel.IsEnabled = true; saveButton.IsEnabled = true; cancelButton.Visibility = Visibility.Collapsed; }
    }
    private async Task StepFrame(int direction)
    {
        if (Catalog.Category(paths[0]) != "video")
        { timeline.Value = Math.Clamp(timeline.Value + direction * .1, 0, totalDuration); return; }
        using var busy = busyLine.Begin();
        try
        { var job = Capture(); job.Parameters!["direction"] = direction.ToString(); job.Parameters["time"] = timeline.Value.ToString(CultureInfo.InvariantCulture); var info = await Backend.Preview(job, folder, closing.Token, "--frame-step"); timeline.Value = info.GetProperty("Time").GetDouble(); }
        catch (Exception ex) { status.Text = L.T("逐帧：") + ex.Message; }
    }
    private async Task<JsonElement> PreparePlayback(bool effect, string source, double time, CancellationToken token)
    {
        using var busy = busyLine.Begin();
        var job = effect ? Capture() : new ConversionJob([source], "preview-original", new());
        job.Parameters!["time"] = time.ToString(CultureInfo.InvariantCulture);
        if (!effect)
            job.Parameters["fullPreview"] = "true";
        return await Backend.Preview(job, folder, token, "--playback");
    }
}
