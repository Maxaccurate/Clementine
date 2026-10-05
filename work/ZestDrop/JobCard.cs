using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ZestDrop;

// One background job on the desktop progress stack: waiting → running (pausable) → result.
internal sealed class JobCard : Border
{
    private readonly ActivityIndicator indicator = new();
    private readonly StackPanel controls = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button pauseButton, cancelButton, hideButton;
    private readonly bool converting;
    private readonly string firstName;
    private readonly int count;
    private string stage, file;
    private ActivityIndicator.Session? session;

    public event Action<JobCard>? PauseRequested, ResumeRequested, CancelRequested, HideRequested;
    public JobState State { get; private set; } = JobState.Waiting;
    public bool Paused { get; private set; }
    public bool UserHidden { get; set; }
    public string Heading => stage;
    public string FileName => file;

    public JobCard(ConversionJob job)
    {
        converting = job.Action.StartsWith("convert:");
        firstName = Path.GetFileName(job.Paths[0]);
        count = job.Paths.Length;
        (stage, file) = Labels();
        Background = Brushes.White;
        BorderBrush = new SolidColorBrush(Color.FromRgb(230, 232, 235));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(20, 16, 20, 14);
        Margin = new Thickness(10, 5, 10, 5);
        Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = .12 };
        pauseButton = Action(L.T("暂停"), L.T("暂停此任务，稍后可继续"), () => (Paused ? ResumeRequested : PauseRequested)?.Invoke(this));
        cancelButton = Action(L.T("取消"), L.T("停止此任务并清理未完成的文件"), () => CancelRequested?.Invoke(this));
        hideButton = Action(L.T("隐藏"), L.T("隐藏这张卡片，任务继续在后台运行；可从托盘菜单重新显示"), () => HideRequested?.Invoke(this));
        hideButton.Margin = new Thickness(0);
        var content = new StackPanel();
        content.Children.Add(indicator);
        content.Children.Add(controls);
        Child = content;
        session = indicator.Begin(stage, file);
        session.Report(new JobProgress("queued", 0, 1, file));
        pauseButton.IsEnabled = false; // nothing to pause until it runs
    }

    private (string Stage, string File) Labels() =>
        (converting ? L.T("正在转换格式") : L.T("正在处理文件"), firstName + (count > 1 ? L.F(" 等 {0} 项", count, count - 1) : ""));

    // Re-reads every label in the current interface language.
    public void Relabel()
    {
        (stage, file) = Labels();
        session?.Rename(stage, file);
        SetPaused(Paused);
        cancelButton.Content = L.T("取消");
        cancelButton.ToolTip = L.T("停止此任务并清理未完成的文件");
        hideButton.Content = L.T("隐藏");
        hideButton.ToolTip = L.T("隐藏这张卡片，任务继续在后台运行；可从托盘菜单重新显示");
    }

    private Button Action(string text, string tip, System.Action click)
    {
        var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(0, 0, 8, 0) };
        button.Click += (_, _) => click();
        controls.Children.Add(button);
        return button;
    }

    // Waiting → running: the elapsed clock starts now, not when the job was queued.
    public IProgress<JobProgress> Start()
    {
        session?.Dispose();
        session = indicator.Begin(stage, file);
        State = JobState.Running;
        pauseButton.IsEnabled = true;
        return session;
    }

    public void SetPaused(bool value)
    {
        Paused = value;
        pauseButton.Content = value ? L.T("继续") : L.T("暂停");
        pauseButton.ToolTip = value ? L.T("从暂停处继续处理") : L.T("暂停此任务，稍后可继续");
        session?.SetPaused(value);
    }

    public void SetStopping()
    {
        State = JobState.Stopping;
        pauseButton.IsEnabled = cancelButton.IsEnabled = false;
        Paused = false;
        session?.SetPaused(false);
    }

    public void Complete(string title, string detail)
    {
        session?.Dispose();
        session = null;
        State = JobState.Done;
        Paused = false;
        indicator.Complete(title, detail);
        controls.Visibility = Visibility.Collapsed;
    }
}

internal enum JobState { Waiting, Running, Stopping, Done }
