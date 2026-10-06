using System.Collections.Generic;
using System.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ZestDrop;

internal static class Program
{
    private static string WritableResultPath()
    {
        string preferred = Path.Combine(AppContext.BaseDirectory, "last-cli-result.json");
        try
        {
            using (File.Open(preferred, FileMode.OpenOrCreate, FileAccess.Write)) { }
            return preferred;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Directory.CreateDirectory(Journal.DirectoryPath);
            return Path.Combine(Journal.DirectoryPath, "last-cli-result.json");
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--debug-tool-render")
        {
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var window = new ToolWindow([args[2]], Catalog.Definition(args[1], Catalog.Category(args[2])), Backend.Execute);
            window.Loaded += async (_, _) =>
            {
                await Task.Delay(3000);
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var background = new System.Windows.Media.DrawingVisual();
                using (var dc = background.RenderOpen())
                    dc.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                bitmap.Render(background);
                bitmap.Render((System.Windows.Media.Visual)window.Content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(args[3]))
                    encoder.Save(stream);
                window.Close();
            };
            testApp.Run(window);
            return 0;
        }
        if (args.Length == 3 && args[0] == "--debug-media-test")
        {
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var media = new System.Windows.Controls.MediaElement { LoadedBehavior = System.Windows.Controls.MediaState.Manual, UnloadedBehavior = System.Windows.Controls.MediaState.Stop, Height = 220 };
            var slider = new System.Windows.Controls.Slider();
            string? error = null;
            var transport = new MediaTransport(media, slider, [args[1]], Catalog.Category(args[1]) == "audio", false, (_, _, _, _) => throw new InvalidOperationException("Unexpected compatibility fallback"), CancellationToken.None, text => error = text);
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(media);
            panel.Children.Add(transport);
            media.Volume = 0;
            var window = new Window { Content = panel, Width = 620, Height = 420, ShowActivated = false, ShowInTaskbar = false, Title = L.T("ZestDrop 播放器检查") };
            window.Loaded += async (_, _) =>
            {
                bool passed = false;
                double advanced = 0, paused = 0, sought = 0;
                try
                {
                    transport.SetDuration(35);
                    await transport.Toggle();
                    for (int i = 0; i < 100 && !transport.IsPlaying; i++)
                        await Task.Delay(100);
                    await Task.Delay(1000);
                    advanced = media.Position.TotalSeconds;
                    transport.Pause();
                    paused = media.Position.TotalSeconds;
                    await Task.Delay(350);
                    bool holds = Math.Abs(media.Position.TotalSeconds - paused) < .15;
                    await transport.Seek(10);
                    await Task.Delay(400);
                    sought = media.Position.TotalSeconds;
                    passed = advanced > .1 && holds && Math.Abs(sought - 10) < .5 && error == null;
                }
                catch (Exception ex) { error = ex.Message; }
                finally { transport.Dispose(); File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed, advanced, paused, sought, error })); window.Close(); testApp.Shutdown(); }
            };
            window.Show();
            testApp.Run();
            return 0;
        }
        if (args.Length == 3 && args[0] == "--capabilities")
        {
            string[] files = [args[1]];
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { Category = Catalog.Category(args[1]), Conversions = Catalog.Options(files, false), Tools = Catalog.Options(files, true) }));
            return 0;
        }
        if (args.Length == 4 && args[0] == "--office-export")
        {
            try
            { OfficeBridge.Export(args[1], args[2], args[3]); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        if (args.Length == 3 && args[0] == "--worker")
        {
            Backend.Run(["--job", args[1], args[2]]).GetAwaiter().GetResult();
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--convert")
        {
            string folder = Path.Combine(Journal.DirectoryPath, "cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string request = Path.Combine(folder, "request.json"), response = WritableResultPath();
            File.WriteAllText(request, JsonSerializer.Serialize(new ConversionJob(args.Skip(2).ToArray(), "convert:" + args[1])));
            Backend.Run(["--job", request, response]).GetAwaiter().GetResult();
            var result = JsonSerializer.Deserialize<BatchResult>(File.ReadAllText(response))!;
            File.Delete(request);
            Directory.Delete(folder);
            return result.Files.All(f => f.Error == null) ? 0 : 1;
        }
        if (args.Length >= 3 && args[0] == "--debug-tool")
        {
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            testApp.Run(new ToolWindow(args.Skip(2).ToArray(), Catalog.Definition(args[1], Catalog.Category(args[2])), Backend.Execute));
            return 0;
        }
        if (args.Length == 5 && args[0] == "--debug-wheel-render")
        {
            // --debug-wheel-render <formats|tools> <file> <operation id to highlight> <out.png>
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            testApp.Dispatcher.InvokeAsync(() =>
            {
                var wheel = new DropWheel((_, _) => { });
                wheel.Preview([args[2]], args[1] == "tools", args[3]);
                var root = (FrameworkElement)wheel.Content;
                root.Measure(new Size(380, 380));
                root.Arrange(new Rect(0, 0, 380, 380));
                root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1140, 1140, 288, 288, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var stream = File.Create(args[4]))
                    encoder.Save(stream);
                testApp.Shutdown();
            });
            testApp.Run();
            return 0;
        }
        if (args.Length == 2 && args[0] == "--debug-indicator")
        {
            // Renders the progress stack in several states to <prefix>-<state>.png for visual checks.
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var stack = new TaskIndicatorWindow(headless: true);
            void Render(string state)
            {
                var root = (FrameworkElement)stack.Content;
                root.Width = stack.Width;
                root.Measure(new Size(stack.Width, double.PositiveInfinity));
                root.Arrange(new Rect(root.DesiredSize));
                root.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                var backdrop = new System.Windows.Media.DrawingVisual();
                using (var dc = backdrop.RenderOpen())
                    dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 217, 221)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                bitmap.Render(backdrop);
                bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = File.Create($"{args[1]}-{state}.png");
                encoder.Save(stream);
            }
            testApp.Dispatcher.InvokeAsync(() =>
            {
                var first = stack.Add(new ConversionJob(["B&W"], "pack:zip"));
                first.Start().Report(new JobProgress("processing", 0, 1, "B&W"));
                Render("single");
                var second = stack.Add(new ConversionJob([L.T("假期视频.mp4")], "convert:webm"));
                second.Start().Report(new JobProgress("processing", 0, 1, L.T("假期视频.mp4")));
                second.SetPaused(true);
                var third = stack.Add(new ConversionJob([L.T("照片 1.heic"), L.T("照片 2.heic"), L.T("照片 3.heic")], "convert:jpg"));
                stack.Update();
                Render("expanded");
                stack.SetExpanded(false);
                Render("collapsed");
                stack.SetExpanded(true);
                stack.HideCard(first);
                Render("one-hidden");
                testApp.Shutdown();
            });
            testApp.Run();
            return 0;
        }
        if (args.Length == 2 && args[0] == "--debug-welcome")
        {
            // Renders the welcome window to <prefix>-en.png and <prefix>-zh.png.
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            testApp.Dispatcher.InvokeAsync(async () =>
            {
                foreach (var language in new[] { "en", "zh" })
                {
                    L.UseForSession(language);
                    var window = new WelcomeWindow { Left = 40, Top = 40, ShowActivated = false };
                    window.Show();
                    await Task.Delay(700);
                    window.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    var background = new System.Windows.Media.DrawingVisual();
                    using (var dc = background.RenderOpen())
                        dc.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                    bitmap.Render(background);
                    bitmap.Render((System.Windows.Media.Visual)window.Content);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var stream = File.Create($"{args[1]}-{language}.png"))
                        encoder.Save(stream);
                    window.Close();
                }
                testApp.Shutdown();
            });
            testApp.Run();
            return 0;
        }
        if (args.Length == 2 && args[0] == "--debug-job")
        {
            var testApp = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var indicator = new TaskIndicatorWindow();
            var job = JsonSerializer.Deserialize<ConversionJob>(File.ReadAllText(args[1]))!;
            testApp.Dispatcher.InvokeAsync(async () =>
            {
                var card = indicator.Add(job);
                try
                {
                    var result = await Backend.Execute(job, CancellationToken.None, card.Start());
                    var error = result.Files.FirstOrDefault(f => f.Error != null)?.Error;
                    indicator.Finish(card, error == null ? L.T("处理完成") : L.T("处理未完成"), error ?? Path.GetFileName(result.Files.Last().Output) ?? L.T("新文件已保存"));
                }
                catch (Exception ex) { indicator.Finish(card, L.T("处理未完成"), ex.Message); }
                await Task.Delay(5000);
                indicator.Close();
                testApp.Shutdown();
            });
            testApp.Run();
            return 0;
        }
        using var mutex = new Mutex(true, @"Local\ZestDrop_20261003", out bool first);
        // Both copies create the same named event, so it works whichever starts first.
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ZestDrop_20261003_Show");
        if (!first)
        {
            // Launching ZestDrop again (for example from the Start menu) wakes the running copy and shows its window.
            Native.AllowSetForegroundWindow(-1);
            showRequest.Set();
            return 0;
        }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var resident = new Resident(app, showRequest);
        app.Run();
        return 0;
    }
}

internal static class Notices
{
    // Shipped beside the executable by build.ps1; lists every bundled component and its license.
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
}

internal static class Journal
{
    // Keep logs and scratch folders in the user profile so a read-only install folder cannot break startup.
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZestDrop", "logs");
    private const long MaxLogBytes = 4 * 1024 * 1024;
    private static readonly object Sync = new();
    public static void Write(string kind, object? data = null)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string log = Path.Combine(DirectoryPath, "events.jsonl");
                var info = new FileInfo(log);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Move(log, Path.Combine(DirectoryPath, "events.1.jsonl"), true);
                File.AppendAllText(log, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, data }) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    // Remove scratch folders left behind by crashes or forced exits.
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath))
                return;
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var folder in new DirectoryInfo(DirectoryPath).EnumerateDirectories())
            {
                if (folder.Name.Split('-')[0] is not ("job" or "preview" or "qa" or "cli") || folder.LastWriteTimeUtc > cutoff)
                    continue;
                try
                { folder.Delete(true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed class Resident : IDisposable
{
    private readonly System.Windows.Application app;
    private readonly DropWheel wheel;
    private readonly TaskIndicatorWindow taskIndicator = new();
    private readonly DispatcherTimer timer;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Channel<QueuedJob> queue = Channel.CreateBounded<QueuedJob>(new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource stopping = new();
    private readonly DragShortcutTracker dragShortcuts = new(Native.GetSystemMetrics(68), Native.GetSystemMetrics(69));
    private bool f8, f9, escape, mouse, releasePending;
    private DateTime activated = DateTime.MinValue;
    private string? lastOutput;
    private bool disposed;
    // Up to this many jobs run at once; more wait their turn. A paused job gives its slot to the next one.
    private const int MaxParallelJobs = 2;
    private readonly SemaphoreSlim slots = new(MaxParallelJobs);
    private readonly List<RunningJob> jobs = [];
    private sealed class RunningJob(QueuedJob queued, CancellationTokenSource cancellation)
    {
        public readonly QueuedJob Queued = queued;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public readonly string Token = Guid.NewGuid().ToString("N")[..12];
        public JobCard? Card;
        public Process? Worker;
        public TempSweep? Sweep;
        public ProcessTreePause? Pause;
        public bool HoldsSlot, Ended;
    }
    private sealed record QueuedJob(ConversionJob Job, TaskCompletionSource<BatchResult>? Completion = null, CancellationToken Cancellation = default, IProgress<JobProgress>? Progress = null) { public volatile bool Started; }

    private WelcomeWindow? welcome;

    public Resident(System.Windows.Application app, EventWaitHandle showRequest)
    {
        this.app = app;
        Journal.Prune();
        wheel = new DropWheel(BeginOperation);
        trayIcon = AppIcon.CreateTrayIcon();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Visible = true };
        BuildTrayMenu();
        // The tray menu, tooltip and progress stack switch language immediately; open tool windows keep theirs.
        L.Changed += () =>
        {
            BuildTrayMenu();
            taskIndicator.Relabel();
            Notify("ZestDrop", L.T("已切换为简体中文"));
        };
        taskIndicator.PauseRequested += card => { if (jobs.FirstOrDefault(j => j.Card == card) is { } job) PauseJob(job); };
        taskIndicator.ResumeRequested += card => { if (jobs.FirstOrDefault(j => j.Card == card) is { } job) ResumeJob(job); };
        taskIndicator.CancelRequested += card => { if (jobs.FirstOrDefault(j => j.Card == card) is { } job) CancelJob(job); };
        tray.DoubleClick += (_, _) => ShowWelcome();
        timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += Poll;
        timer.Start();
        _ = WorkQueue();
        Journal.Write("Started", new { noMainWindow = true, modifierShortcuts = true, dragFunctionKeys = true });
        // ZestDrop only starts when someone launches it, so show how to use it. People who prefer the quiet start
        // can switch the window off; they still get the tray notification.
        if (Settings.GetBool(WelcomeWindow.HideAtStartKey, false))
            Notify(L.T("ZestDrop 已运行"), L.T("拖文件 + Shift：转换；Ctrl+Shift：工具。拖拽时也可按 F8/F9。"));
        else
            ShowWelcome();
        WatchForSecondLaunch(showRequest);
    }

    // Shows the welcome window, or brings it to the front if it is already open.
    private void ShowWelcome()
    {
        if (disposed)
            return;
        if (welcome == null)
        {
            welcome = new WelcomeWindow();
            welcome.Closed += (_, _) => welcome = null;
            welcome.Show();
        }
        if (welcome.WindowState == WindowState.Minimized)
            welcome.WindowState = WindowState.Normal;
        // Toggling Topmost reliably raises the window above whatever was in front, without keeping it there.
        welcome.Topmost = true;
        welcome.Activate();
        welcome.Topmost = false;
    }

    private void WatchForSecondLaunch(EventWaitHandle showRequest)
    {
        var watcher = new Thread(() =>
        {
            var handles = new WaitHandle[] { showRequest, stopping.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
                app.Dispatcher.InvokeAsync(ShowWelcome);
        })
        { IsBackground = true, Name = "ZestDrop second-launch watcher" };
        watcher.Start();
    }

    private void OpenNotices()
    {
        if (File.Exists(Notices.Path))
            Process.Start(new ProcessStartInfo(Notices.Path) { UseShellExecute = true });
    }

    private void BuildTrayMenu()
    {
        tray.Text = L.T("ZestDrop · 拖文件 + Shift 转换 / Ctrl+Shift 工具");
        var old = tray.ContextMenuStrip;
        var items = new Forms.ContextMenuStrip();
        items.Items.Add(L.T("使用方式：拖文件 + Shift / Ctrl+Shift"), null, (_, _) => ShowWelcome());
        items.Items.Add(new Forms.ToolStripSeparator());
        var showProgress = items.Items.Add(L.T("显示处理进度"), null, (_, _) => taskIndicator.ShowAll());
        var pauseAll = items.Items.Add(L.T("暂停全部任务"), null, (_, _) =>
        {
            bool resume = !jobs.Any(CanPause) && jobs.Any(j => j.Pause != null);
            foreach (var job in jobs.ToList())
                if (resume) ResumeJob(job); else PauseJob(job);
        });
        var cancelAll = items.Items.Add(L.T("取消全部任务"), null, (_, _) => { foreach (var job in jobs.ToList()) CancelJob(job); });
        items.Items.Add(L.T("第三方组件许可"), null, (_, _) => OpenNotices());
        items.Items.Add(L.T("最近输出所在文件夹"), null, (_, _) => { if (lastOutput != null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{lastOutput}\"") { UseShellExecute = true }); });
        items.Items.Add(new Forms.ToolStripSeparator());
        // Language names are shown in their own language so either can be found whatever is active.
        var language = new Forms.ToolStripMenuItem(L.T("语言") + " / Language");
        foreach (var (code, name) in new[] { ("zh", "简体中文"), ("en", "English") })
            language.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) => L.Language = code) { Checked = L.Language == code });
        items.Items.Add(language);
        items.Items.Add(L.T("退出 ZestDrop"), null, (_, _) => app.Shutdown());
        items.Opening += (_, _) =>
        {
            bool canPause = jobs.Any(CanPause), canResume = jobs.Any(j => j.Pause != null);
            showProgress.Enabled = cancelAll.Enabled = jobs.Count > 0;
            pauseAll.Enabled = canPause || canResume;
            pauseAll.Text = !canPause && canResume ? L.T("继续全部任务") : L.T("暂停全部任务");
        };
        tray.ContextMenuStrip = items;
        old?.Dispose();
    }

    private void Activate(bool tools, string reason, bool modifier = false)
    {
        if (wheel.IsVisible)
        {
            if (wheel.ToolsMode == tools)
                return;
            activated = DateTime.UtcNow;
            releasePending = false;
            wheel.ChangeMode(tools);
            Journal.Write("WheelModeChanged", new { reason, tools });
            return;
        }
        if (!modifier && (DateTime.UtcNow - activated).TotalMilliseconds < 200)
            return;
        activated = DateTime.UtcNow;
        releasePending = false;
        Native.GetCursorPos(out var point);
        wheel.OpenAt(point, tools);
        Journal.Write("WheelOpened", new { reason, tools, mouseDown = Native.Down(1), point.X, point.Y });
        if (modifier)
        {
            long instance = wheel.Instance;
            var confirmation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            confirmation.Tick += (_, _) => { confirmation.Stop(); if (wheel.IsVisible && wheel.Instance == instance && !wheel.HasFileDrag) wheel.Dismiss("NotFileDrag"); };
            confirmation.Start();
        }
    }

    private void Poll(object? sender, EventArgs e)
    {
        bool nextF8 = Native.Down(0x77), nextF9 = Native.Down(0x78), nextEscape = Native.Down(0x1B), nextMouse = Native.Down(1);
        Native.GetCursorPos(out var cursor);
        var request = dragShortcuts.Update(nextMouse, Native.Down(0x10), Native.Down(0x11), Native.Down(0x12), cursor.X, cursor.Y, nextMouse && !mouse && Native.ShellFileWindow());
        if (nextEscape && !escape)
            dragShortcuts.Cancel();
        else if (request != DragMenuRequest.None)
            Activate(request == DragMenuRequest.Tools, "DragModifier", true);
        // F8/F9 are only observed while the mouse is held, never registered as global hotkeys,
        // so other applications (Excel recalculation, debugger breakpoints) still receive them.
        if (nextF8 && !f8 && nextMouse)
            Activate(false, "DragFunctionKey", true);
        if (nextF9 && !f9 && nextMouse)
            Activate(true, "DragFunctionKey", true);
        if (nextEscape && !escape && wheel.IsVisible)
            wheel.Dismiss("Escape");
        if (wheel.IsVisible && mouse && !nextMouse && !releasePending)
        {
            releasePending = true;
            var instance = wheel.Instance;
            // Let the OLE Drop callback finish before closing an uncommitted wheel.
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            delay.Tick += (_, _) => { delay.Stop(); if (wheel.IsVisible && wheel.Instance == instance) wheel.Dismiss("ReleasedOutside"); };
            delay.Start();
        }
        if (wheel.IsVisible && !nextMouse && (DateTime.UtcNow - activated).TotalSeconds > 10)
            wheel.Dismiss("IdleTimeout");
        f8 = nextF8;
        f9 = nextF9;
        escape = nextEscape;
        mouse = nextMouse;
    }

    private void Enqueue(ConversionJob job)
    {
        if (!queue.Writer.TryWrite(new QueuedJob(job)))
        { Notify(L.T("任务队列已满"), L.T("请等正在处理的文件完成后重试。")); return; }
        Journal.Write("Queued", new { count = job.Paths.Length, job.Action });
    }

    private async Task<BatchResult> EnqueueTool(ConversionJob job, CancellationToken token, IProgress<JobProgress>? progress)
    {
        var completion = new TaskCompletionSource<BatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new QueuedJob(job, completion, token, progress);
        progress?.Report(new JobProgress("queued", 0, job.Paths.Length, Path.GetFileName(job.Paths[0])));
        using var registration = token.Register(() => { if (!queued.Started) completion.TrySetCanceled(token); });
        await queue.Writer.WriteAsync(queued, token);
        return await completion.Task;
    }

    private void BeginOperation(string[] paths, Operation operation)
    {
        if (operation.Tool && ((operation.Fields?.Length ?? 0) > 0 || operation.Ordered || Catalog.Category(paths[0]) is "audio" or "video"))
        {
            var tool = new ToolWindow(paths, operation, EnqueueTool);
            tool.Show();
            tool.Activate();
        }
        else
            Enqueue(new ConversionJob(paths, operation.Id));
    }

    private async Task WorkQueue()
    {
        try
        {
            // Every job gets a card straight away (waiting, if all slots are busy) and then runs on its own.
            await foreach (var queued in queue.Reader.ReadAllAsync(stopping.Token))
            {
                var job = new RunningJob(queued, CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, queued.Cancellation));
                if (queued.Progress == null)
                    job.Card = taskIndicator.Add(queued.Job);
                jobs.Add(job);
                _ = RunJob(job);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunJob(RunningJob job)
    {
        var queued = job.Queued;
        string session = "", request = "", response = "";
        try
        {
            await slots.WaitAsync(job.Cancellation.Token);
            job.HoldsSlot = true;
            queued.Started = true;
            var progress = queued.Progress ?? job.Card!.Start();
            taskIndicator.Update();
            session = Path.Combine(Journal.DirectoryPath, "job-" + job.Token);
            Directory.CreateDirectory(session);
            request = Path.Combine(session, "request.json");
            response = Path.Combine(session, "result.json");
            File.WriteAllText(request, JsonSerializer.Serialize(queued.Job));
            job.Sweep = new TempSweep(queued.Job.Paths, job.Token);
            using var watch = Backend.WatchProgress(response, progress);
            var start = Backend.StartInfo("--job", request, response);
            // The worker tags its staging files with this id, so cleanup after a cancel touches only this job's files.
            start.Environment["ZESTDROP_JOB"] = job.Token;
            using var process = Process.Start(start) ?? throw new IOException(L.T("无法启动本地处理进程"));
            job.Worker = process;
            var standardError = process.StandardError.ReadToEndAsync();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            // No fixed time limit: long video encodes are legitimate, and every job can be cancelled.
            try
            { await process.WaitForExitAsync(job.Cancellation.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
            await standardOutput;
            string engineError = await standardError;
            if (process.ExitCode != 0 || !File.Exists(response))
                throw new IOException(string.IsNullOrWhiteSpace(engineError) ? L.T("处理引擎未正常完成") : engineError);
            var result = JsonSerializer.Deserialize<BatchResult>(File.ReadAllText(response))!;
            queued.Completion?.TrySetResult(result);
            var success = result.Files.Where(x => x.Output != null).ToArray();
            if (success.Length > 0)
                lastOutput = success[^1].Output;
            int failures = result.Files.Length - success.Length;
            Journal.Write("Completed", new { queued.Job.Action, count = result.Files.Length, success = success.Length, failures, errors = result.Files.Where(x => x.Error != null).Select(x => x.Error) });
            string detail = failures == 0 ? L.T("新文件已保存到原文件夹。") : result.Files.First(x => x.Error != null).Error!;
            string output = success.Length > 0 ? Path.GetFileName(success[^1].Output) ?? L.T("新文件已保存") : L.T("新文件已保存");
            bool cardShown = job.Card == null || taskIndicator.Finish(job.Card, failures == 0 ? L.T("处理完成") : L.F("成功 {0} 项 · 失败 {1} 项", success.Length, failures), failures == 0 ? output : detail);
            if (queued.Progress != null || failures > 0 || !cardShown)
                Notify(L.F("完成 {0} 项", success.Length) + (failures == 0 ? "" : L.F(" · 失败 {0} 项", failures)), failures == 0 && !cardShown ? output : detail);
        }
        catch (OperationCanceledException)
        {
            if (job.Sweep != null)
                await job.Sweep.Run();
            queued.Completion?.TrySetCanceled();
            bool cardShown = job.Card == null || taskIndicator.Finish(job.Card, L.T("处理已停止"), L.T("此任务已取消。"));
            if (!stopping.IsCancellationRequested && (job.Worker != null || !cardShown))
                Notify(L.T("处理已停止"), job.Worker != null ? L.T("此任务已取消，未完成的临时文件已清理。") : L.T("此任务已取消。"));
        }
        catch (Exception ex)
        {
            if (job.Sweep != null)
                await job.Sweep.Run();
            queued.Completion?.TrySetException(ex);
            if (job.Card != null)
                taskIndicator.Finish(job.Card, L.T("处理未完成"), ex.Message);
            Journal.Write("JobFailed", new { ex.Message });
            Notify(L.T("转换未完成"), ex.Message);
        }
        finally
        {
            job.Ended = true;
            job.Pause?.Dispose();
            job.Pause = null;
            if (job.HoldsSlot)
                slots.Release();
            job.HoldsSlot = false;
            jobs.Remove(job);
            job.Cancellation.Dispose();
            // Only remove files created for this specific worker request.
            foreach (var file in new[] { request, response, response + ".progress", response + ".progress.tmp" })
                if (Path.IsPathRooted(file) && File.Exists(file))
                    File.Delete(file);
            if (session.Length > 0 && Directory.Exists(session) && !Directory.EnumerateFileSystemEntries(session).Any())
                Directory.Delete(session);
        }
    }

    private static bool CanPause(RunningJob job) => !job.Ended && job.Card is { State: JobState.Running } && job.Worker is { HasExited: false } && job.Pause == null;

    private void PauseJob(RunningJob job)
    {
        if (!CanPause(job))
            return;
        try
        {
            job.Pause = ProcessTreePause.Suspend(job.Worker!);
            job.Card!.SetPaused(true);
            // A paused job frees its slot so a waiting job can start.
            if (job.HoldsSlot)
            { job.HoldsSlot = false; slots.Release(); }
            Journal.Write("JobPaused", new { job.Queued.Job.Action, processes = job.Pause.Count });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Notify(L.T("无法暂停"), ex.Message); }
        taskIndicator.Update();
    }

    private void ResumeJob(RunningJob job)
    {
        if (job.Pause == null || job.Ended)
            return;
        job.Pause.Dispose();
        job.Pause = null;
        job.Card?.SetPaused(false);
        // Take a slot back if one is free; otherwise run anyway, since the user asked for it.
        job.HoldsSlot = slots.Wait(0);
        Journal.Write("JobResumed", new { job.Queued.Job.Action });
        taskIndicator.Update();
    }

    private void CancelJob(RunningJob job)
    {
        if (job.Ended)
            return;
        job.Card?.SetStopping();
        taskIndicator.Update();
        // Killing works on a suspended tree too, so a paused job can be cancelled directly.
        try { job.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void Notify(string title, string message)
    {
        if (disposed)
            return;
        app.Dispatcher.InvokeAsync(() =>
        {
            if (disposed)
                return;
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = message.Length > 220 ? message[..220] : message;
            tray.ShowBalloonTip(3000);
        });
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Stop();
        stopping.Cancel();
        queue.Writer.TryComplete();
        foreach (var job in jobs.ToList())
        {
            try
            {
                if (job.Worker is { HasExited: false } worker)
                { worker.Kill(true); job.Sweep?.Run().Wait(TimeSpan.FromSeconds(3)); }
            }
            catch (InvalidOperationException) { }
            job.Pause?.Dispose();
        }
        tray.Dispose();
        trayIcon.Dispose();
        welcome?.Close();
        wheel.Close();
        taskIndicator.Close();
        Journal.Write("Stopped");
    }
}
