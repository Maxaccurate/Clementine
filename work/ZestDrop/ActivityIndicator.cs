using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ZestDrop;

// Used by both the tool's preview overlay and the desktop task toast.
internal sealed class ActivityIndicator : StackPanel
{
    private readonly TextBlock heading = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = UiTheme.Ink };
    private readonly TextBlock description = new() { FontSize = 12, Foreground = UiTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock timing = new() { FontSize = 11, Foreground = UiTheme.Muted, Margin = new Thickness(0, 7, 0, 0) };
    private readonly Grid track = new() { Height = 3, ClipToBounds = true, Background = UiTheme.Tint, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border fill = new() { Height = 3, Background = UiTheme.Accent, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TranslateTransform movement = new();
    private Stopwatch elapsed = new();
    private readonly Dictionary<long, Entry> sessions = [];
    private long activeId;
    private string stage = "";
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private long version;
    private string detail = "";
    private JobProgress? progress;
    private bool running, indeterminate, paused;

    public ActivityIndicator()
    {
        fill.RenderTransform = movement;
        track.Children.Add(fill);
        Children.Add(heading);
        Children.Add(description);
        Children.Add(track);
        Children.Add(timing);
        track.SizeChanged += (_, _) => { indeterminate = false; Paint(); };
        clock.Tick += (_, _) => UpdateText();
        Unloaded += (_, _) => { clock.Stop(); movement.BeginAnimation(TranslateTransform.XProperty, null); };
        Loaded += (_, _) => { if (running) clock.Start(); Paint(); };
        Visibility = Visibility.Collapsed;
    }
    public Session Begin(string stage, string description)
    {
        version++;
        var entry = new Entry(stage, description);
        sessions[version] = entry;
        Select(version, entry);
        return new Session(this, version);
    }
    private sealed class Entry(string stage, string detail)
    {
        public string Stage = stage, Detail = detail;
        public readonly Stopwatch Elapsed = Stopwatch.StartNew();
        public JobProgress? Progress;
        public bool Paused;
    }
    private void Select(long id, Entry entry)
    {
        activeId = id;
        stage = entry.Stage;
        detail = entry.Detail;
        elapsed = entry.Elapsed;
        progress = entry.Progress;
        paused = entry.Paused;
        running = true;
        Visibility = Visibility.Visible;
        clock.Start();
        UpdateHeading();
        UpdateText();
        Paint();
    }
    private void UpdateHeading() => heading.Text = paused ? L.T("已暂停") : progress?.Phase switch { "queued" => L.T("等待处理"), "completed" => L.T("处理完成"), _ => stage };
    private void UpdateText()
    {
        description.Text = progress?.Current ?? detail;
        description.ToolTip = description.Text;
        string time = elapsed.Elapsed.TotalMinutes >= 1 ? L.F("已用 {0} 分 {1} 秒", (int)elapsed.Elapsed.TotalMinutes, elapsed.Elapsed.Seconds) : L.F("已用 {0} 秒", (int)elapsed.Elapsed.TotalSeconds);
        timing.Text = progress?.Phase == "queued" ? L.T("前面的任务完成后开始") : progress is { Total: > 1 } ? L.F("已处理 {0} / {1} 项  ·  {2}", progress.Processed, progress.Total, time) : time;
    }
    private void Paint()
    {
        bool next = running && !paused && (progress == null || progress.Total <= 1 || progress.Processed == 0) && progress?.Phase != "completed";
        if (next)
        {
            fill.Width = Math.Max(45, track.ActualWidth * .24);
            if (!indeterminate || !movement.HasAnimatedProperties)
                movement.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-fill.Width, Math.Max(1, track.ActualWidth), TimeSpan.FromSeconds(1.35)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            movement.BeginAnimation(TranslateTransform.XProperty, null);
            movement.X = 0;
            fill.Width = Math.Max(0, track.ActualWidth * (progress == null ? 1 : (double)progress.Processed / progress.Total));
        }
        indeterminate = next;
    }
    private void Report(long id, JobProgress value)
    {
        if (!Dispatcher.CheckAccess())
        { Dispatcher.InvokeAsync(() => Report(id, value)); return; }
        if (!sessions.TryGetValue(id, out var entry))
            return;
        entry.Progress = value;
        if (id != activeId)
            return;
        progress = value;
        UpdateHeading();
        UpdateText();
        Paint();
    }
    // Used when the interface language changes while a job is on screen.
    private void Rename(long id, string newStage, string newDetail)
    {
        if (!sessions.TryGetValue(id, out var entry))
            return;
        entry.Stage = newStage;
        entry.Detail = newDetail;
        if (id != activeId)
            return;
        stage = newStage;
        detail = newDetail;
        UpdateHeading();
        UpdateText();
    }
    // Pausing freezes the elapsed clock and the bar; resuming continues from the same time.
    private void Pause(long id, bool value)
    {
        if (!Dispatcher.CheckAccess())
        { Dispatcher.InvokeAsync(() => Pause(id, value)); return; }
        if (!sessions.TryGetValue(id, out var entry) || entry.Paused == value)
            return;
        entry.Paused = value;
        if (value)
            entry.Elapsed.Stop();
        else
            entry.Elapsed.Start();
        if (id != activeId)
            return;
        paused = value;
        UpdateHeading();
        UpdateText();
        Paint();
    }
    private void End(long id)
    {
        if (!sessions.Remove(id, out var entry))
            return;
        entry.Elapsed.Stop();
        if (id != activeId)
            return;
        if (sessions.Count > 0)
        { var next = sessions.MaxBy(x => x.Key); Select(next.Key, next.Value); return; }
        running = false;
        clock.Stop();
        elapsed.Stop();
        movement.BeginAnimation(TranslateTransform.XProperty, null);
        Visibility = Visibility.Collapsed;
    }
    public void Complete(string title, string detail)
    {
        version++;
        foreach (var entry in sessions.Values)
            entry.Elapsed.Stop();
        sessions.Clear();
        running = false;
        paused = false;
        clock.Stop();
        elapsed.Stop();
        progress = null;
        heading.Text = title;
        description.Text = detail;
        description.ToolTip = detail;
        timing.Text = "";
        Visibility = Visibility.Visible;
        Paint();
    }
    internal sealed class Session : IDisposable, IProgress<JobProgress>
    {
        private readonly ActivityIndicator owner;
        private readonly long id;
        internal Session(ActivityIndicator owner, long id) { this.owner = owner; this.id = id; }
        public void Report(JobProgress value) => owner.Report(id, value);
        public void SetPaused(bool value) => owner.Pause(id, value);
        public void Rename(string stage, string detail) => owner.Rename(id, stage, detail);
        public void Dispose() => owner.End(id);
    }
}
