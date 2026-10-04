using System.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopDrop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==3&&args[0]=="--capabilities")
        {
            string[] files=[args[1]];
            File.WriteAllText(args[2],JsonSerializer.Serialize(new{Category=Catalog.Category(args[1]),Conversions=Catalog.Options(files,false),Tools=Catalog.Options(files,true)}));return 0;
        }
        if(args.Length==4&&args[0]=="--office-export")
        {
            try{OfficeBridge.Export(args[1],args[2],args[3]);return 0;}
            catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
        }
        if (args.Length == 3 && args[0] == "--worker")
        {
            Backend.Run(["--job",args[1],args[2]]).GetAwaiter().GetResult();
            return 0;
        }
        if (args.Length >= 3 && args[0] == "--convert")
        {
            string folder=Path.Combine(AppContext.BaseDirectory,"logs","cli-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string request=Path.Combine(folder,"request.json"),response=Path.Combine(AppContext.BaseDirectory,"last-cli-result.json");
            File.WriteAllText(request,JsonSerializer.Serialize(new ConversionJob(args.Skip(2).ToArray(),"convert:"+args[1])));
            Backend.Run(["--job",request,response]).GetAwaiter().GetResult();
            var result=JsonSerializer.Deserialize<BatchResult>(File.ReadAllText(response))!;
            File.Delete(request); Directory.Delete(folder);
            return result.Files.All(f => f.Error == null) ? 0 : 1;
        }
        if(args.Length>=3 && args[0]=="--debug-tool")
        {
            var testApp=new System.Windows.Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
            testApp.Run(new ToolWindow(args.Skip(2).ToArray(),Catalog.Definition(args[1],Catalog.Category(args[2])),Backend.Execute)); return 0;
        }
        if(args.Length==2&&args[0]=="--debug-indicator")
        {
            var testApp=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var indicator=new TaskIndicatorWindow{Title="DesktopDrop 进度提示预览",ShowInTaskbar=true};
            testApp.Dispatcher.InvokeAsync(async()=>
            {
                var progress=indicator.Begin(new ConversionJob(["示例照片.png"],"convert:jpg"));
                progress.Report(args[1]=="batch"?new JobProgress("processing",2,5,"示例照片 3.png"):new JobProgress("processing",0,1,"示例照片.png"));
                await Task.Delay(TimeSpan.FromSeconds(60));indicator.Close();testApp.Shutdown();
            });testApp.Run();return 0;
        }
        if(args.Length==2&&args[0]=="--debug-job")
        {
            var testApp=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var indicator=new TaskIndicatorWindow();var job=JsonSerializer.Deserialize<ConversionJob>(File.ReadAllText(args[1]))!;
            testApp.Dispatcher.InvokeAsync(async()=>
            {
                try
                {
                    var progress=indicator.Begin(job);var result=await Backend.Execute(job,CancellationToken.None,progress);
                    var error=result.Files.FirstOrDefault(f=>f.Error!=null)?.Error;
                    indicator.Finish(error==null?"处理完成":"处理未完成",error??Path.GetFileName(result.Files.Last().Output)??"新文件已保存");
                }
                catch(Exception ex){indicator.Finish("处理未完成",ex.Message);}
                await Task.Delay(5000);indicator.Close();testApp.Shutdown();
            });testApp.Run();return 0;
        }
        using var mutex = new Mutex(true, @"Local\DesktopDrop_20261003", out bool first);
        if (!first) return 0;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var resident = new Resident(app);
        app.Run();
        return 0;
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    public static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
}

internal static class Journal
{
    public static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly object Sync = new();
    public static void Write(string kind, object? data = null)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(Path.Combine(DirectoryPath, "events.jsonl"), JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, data }) + Environment.NewLine);
        }
    }
}

internal sealed class Resident : IDisposable
{
    private readonly System.Windows.Application app;
    private readonly HwndSource messageWindow;
    private readonly DropWheel wheel;
    private readonly TaskIndicatorWindow taskIndicator=new();
    private readonly DispatcherTimer timer;
    private readonly Forms.NotifyIcon tray;
    private readonly Channel<QueuedJob> queue = Channel.CreateBounded<QueuedJob>(new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource stopping = new();
    private readonly bool[] registered = new bool[2];
    private bool f8, f9, escape, mouse, releasePending;
    private DateTime activated = DateTime.MinValue;
    private string? lastOutput;
    private bool disposed;
    private Process? activeWorker;
    private CancellationTokenSource? activeCancellation;
    private sealed record QueuedJob(ConversionJob Job,TaskCompletionSource<BatchResult>? Completion=null,CancellationToken Cancellation=default,IProgress<JobProgress>? Progress=null){public volatile bool Started;}

    public Resident(System.Windows.Application app)
    {
        this.app = app;
        messageWindow = new HwndSource(new HwndSourceParameters("DesktopDrop message receiver") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
        messageWindow.AddHook(Hook);
        wheel = new DropWheel(BeginOperation);
        for (int i = 0; i < 2; i++) registered[i] = Native.RegisterHotKey(messageWindow.Handle, i + 1, 0x4000, (uint)(0x77 + i));
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "DesktopDrop · 拖文件 + F8 转换 / F9 工具", Visible = true };
        var items = new Forms.ContextMenuStrip();
        items.Items.Add("使用方式：拖文件时按 F8 / F9", null, (_, _) => Notify("桌面拖拽转换", "拖文件时按 F8，移到目标格式上松手。F9 打开对应工具。支持图片、视频、音频、文档和压缩包。"));
        items.Items.Add("取消当前任务",null,(_,_)=>{ try { activeCancellation?.Cancel(); } catch(InvalidOperationException) {} });
        items.Items.Add("最近输出所在文件夹", null, (_, _) => { if (lastOutput != null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{lastOutput}\"") { UseShellExecute = true }); });
        items.Items.Add("退出 DesktopDrop", null, (_, _) => app.Shutdown());
        tray.ContextMenuStrip = items;
        tray.DoubleClick += (_, _) => Notify("桌面拖拽转换", "在桌面或资源管理器拖文件，按 F8 转换格式，F9 选择高级工具。");
        timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += Poll; timer.Start();
        _ = WorkQueue();
        Journal.Write("Started", new { noMainWindow = true, f8Registered = registered[0], f9Registered = registered[1] });
        Notify("DesktopDrop 五类文件版已运行", "拖文件 + F8：转换；F9：工具。菜单根据文件类型变化。");
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            Activate(wParam.ToInt32() == 2, "WM_HOTKEY"); handled = true;
        }
        return IntPtr.Zero;
    }

    private void Activate(bool tools, string reason)
    {
        if ((DateTime.UtcNow - activated).TotalMilliseconds < 200) return;
        activated = DateTime.UtcNow; releasePending = false;
        Native.GetCursorPos(out var point);
        wheel.OpenAt(point, tools);
        Journal.Write("WheelOpened", new { reason, tools, mouseDown = Native.Down(1), point.X, point.Y });
    }

    private void Poll(object? sender, EventArgs e)
    {
        bool nextF8 = Native.Down(0x77), nextF9 = Native.Down(0x78), nextEscape = Native.Down(0x1B), nextMouse = Native.Down(1);
        if (nextF8 && !f8) Activate(false, "KeyStateEdge");
        if (nextF9 && !f9) Activate(true, "KeyStateEdge");
        if (nextEscape && !escape && wheel.IsVisible) wheel.Dismiss("Escape");
        if (wheel.IsVisible && mouse && !nextMouse && !releasePending)
        {
            releasePending = true;
            var instance = wheel.Instance;
            // Let the OLE Drop callback finish before closing an uncommitted wheel.
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            delay.Tick += (_, _) => { delay.Stop(); if (wheel.IsVisible && wheel.Instance == instance) wheel.Dismiss("ReleasedOutside"); };
            delay.Start();
        }
        if (wheel.IsVisible && !nextMouse && (DateTime.UtcNow - activated).TotalSeconds > 10) wheel.Dismiss("IdleTimeout");
        f8 = nextF8; f9 = nextF9; escape = nextEscape; mouse = nextMouse;
    }

    private void Enqueue(ConversionJob job)
    {
        if (!queue.Writer.TryWrite(new QueuedJob(job))) { Notify("任务队列已满", "请等正在处理的文件完成后重试。"); return; }
        Journal.Write("Queued", new { count = job.Paths.Length, job.Action });
    }

    private async Task<BatchResult> EnqueueTool(ConversionJob job,CancellationToken token,IProgress<JobProgress>? progress)
    {
        var completion=new TaskCompletionSource<BatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued=new QueuedJob(job,completion,token,progress);progress?.Report(new JobProgress("queued",0,job.Paths.Length,Path.GetFileName(job.Paths[0])));
        using var registration=token.Register(()=>{if(!queued.Started)completion.TrySetCanceled(token);});
        await queue.Writer.WriteAsync(queued,token);
        return await completion.Task;
    }

    private void BeginOperation(string[] paths,Operation operation)
    {
        if(operation.Tool && ((operation.Fields?.Length??0)>0 || operation.Ordered))
        {
            var tool=new ToolWindow(paths,operation,EnqueueTool); tool.Show(); tool.Activate();
        }
        else Enqueue(new ConversionJob(paths,operation.Id));
    }

    private async Task WorkQueue()
    {
        try
        {
            await foreach (var queued in queue.Reader.ReadAllAsync(stopping.Token))
            {
                queued.Started=true;
                if(queued.Cancellation.IsCancellationRequested){queued.Completion?.TrySetCanceled(queued.Cancellation);continue;}
                var job=queued.Job;
                var progress=queued.Progress??taskIndicator.Begin(job);
                var session = Path.Combine(Journal.DirectoryPath, "job-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(session);
                string request = Path.Combine(session, "request.json"), response = Path.Combine(session, "result.json");
                File.WriteAllText(request, JsonSerializer.Serialize(job));
                try
                {
                    using var watch=Backend.WatchProgress(response,progress);
                    var start = Backend.StartInfo("--job",request,response);
                    using var process = Process.Start(start) ?? throw new IOException("无法启动本地处理进程");
                    activeWorker = process;
                    var standardError=process.StandardError.ReadToEndAsync();
                    var standardOutput=process.StandardOutput.ReadToEndAsync();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token,queued.Cancellation);
                    activeCancellation=timeout;timeout.CancelAfter(TimeSpan.FromMinutes(30));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
                    await standardOutput;
                    string engineError=await standardError;
                    if (process.ExitCode != 0 || !File.Exists(response)) throw new IOException(string.IsNullOrWhiteSpace(engineError)?"处理引擎未正常完成":engineError);
                    var result = JsonSerializer.Deserialize<BatchResult>(File.ReadAllText(response))!;
                    queued.Completion?.TrySetResult(result);
                    var success = result.Files.Where(x => x.Output != null).ToArray();
                    if (success.Length > 0) lastOutput = success[^1].Output;
                    int failures = result.Files.Length - success.Length;
                    Journal.Write("Completed", new { job.Action, count = result.Files.Length, success = success.Length, failures, files = result.Files });
                    string detail = failures == 0 ? "新文件已保存到原文件夹。" : result.Files.First(x => x.Error != null).Error!;
                    if(queued.Progress!=null||failures>0)Notify($"完成 {success.Length} 项" + (failures == 0 ? "" : $" · 失败 {failures} 项"), detail);
                    if(queued.Progress==null)taskIndicator.Finish(failures==0?"处理完成":$"成功 {success.Length} 项 · 失败 {failures} 项",failures==0?Path.GetFileName(lastOutput)??"新文件已保存":detail);
                }
                catch (OperationCanceledException) { queued.Completion?.TrySetCanceled();if(queued.Progress==null)taskIndicator.Finish("处理已停止","此任务已取消或达到处理时限。");if (!stopping.IsCancellationRequested) Notify("处理已停止", "此任务已取消或达到处理时限。"); }
                catch (Exception ex) { queued.Completion?.TrySetException(ex);if(queued.Progress==null)taskIndicator.Finish("处理未完成",ex.Message);Journal.Write("JobFailed", new { ex.Message }); Notify("转换未完成", ex.Message); }
                finally
                {
                    activeWorker = null;activeCancellation=null;
                    // Only remove files created for this specific worker request.
                    if (File.Exists(request)) File.Delete(request);
                    if (File.Exists(response)) File.Delete(response);
                    if (File.Exists(response+".progress")) File.Delete(response+".progress");
                    if (File.Exists(response+".progress.tmp")) File.Delete(response+".progress.tmp");
                    if (!Directory.EnumerateFileSystemEntries(session).Any()) Directory.Delete(session);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Notify(string title, string message)
    {
        if (disposed) return;
        app.Dispatcher.InvokeAsync(()=>
        {
            if(disposed) return;
            tray.BalloonTipTitle = title; tray.BalloonTipText = message.Length > 220 ? message[..220] : message;
            tray.ShowBalloonTip(3000);
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); stopping.Cancel(); queue.Writer.TryComplete();
        try { if (activeWorker != null && !activeWorker.HasExited) activeWorker.Kill(true); } catch (InvalidOperationException) { }
        for (int i = 0; i < 2; i++) if (registered[i]) Native.UnregisterHotKey(messageWindow.Handle, i + 1);
        tray.Dispose(); wheel.Close();taskIndicator.Close(); messageWindow.Dispose(); Journal.Write("Stopped");
    }
}
