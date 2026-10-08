using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ZestDrop;

/// <summary>
/// A thin line along the top of a preview that shows something is being worked on. It stays out of the way:
/// it appears only if the work takes longer than <see cref="ShowDelay"/>, so quick updates show nothing at all,
/// and once it appears it stays for <see cref="MinVisible"/> so it never flashes. It never covers the picture.
/// </summary>
internal sealed class BusyLine : Border
{
    private readonly Border fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Background = UiTheme.Accent, CornerRadius = new CornerRadius(1.5) };
    private readonly TranslateTransform slide = new();
    private readonly DispatcherTimer showTimer = new(), hideTimer = new();
    private int active;
    private bool determinate;
    private double progress;
    private DateTime shownAt;

    /// <summary>How long work must run before the line appears.</summary>
    public TimeSpan ShowDelay { get; set; } = TimeSpan.FromMilliseconds(280);
    /// <summary>The least time the line stays once it has appeared.</summary>
    public TimeSpan MinVisible { get; set; } = TimeSpan.FromMilliseconds(420);
    /// <summary>True while the line is meant to be on screen (it fades out shortly after this turns false).</summary>
    public bool IsShown { get; private set; }
    /// <summary>How far a batch has got (0 to 1), or null while the line just keeps moving.</summary>
    public double? Fraction => determinate ? progress : null;

    public BusyLine()
    {
        Height = 3;
        VerticalAlignment = VerticalAlignment.Top;
        IsHitTestVisible = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        Background = new SolidColorBrush(Color.FromArgb(46, 213, 84, 24));
        fill.RenderTransform = slide;
        Child = fill;
        showTimer.Tick += (_, _) => { showTimer.Stop(); if (active > 0) Show(); };
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); if (active == 0) Hide(); };
        SizeChanged += (_, _) => { if (IsShown) Paint(); };
    }

    /// <summary>Marks the start of some work. Dispose the result when it is done; it can also receive progress.</summary>
    public Session Begin()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(Begin);
        active++;
        hideTimer.Stop();
        if (!IsShown)
        {
            if (ShowDelay <= TimeSpan.Zero)
                Show();
            else if (!showTimer.IsEnabled)
            { showTimer.Interval = ShowDelay; showTimer.Start(); }
        }
        return new Session(this);
    }

    private void End()
    {
        if (!Dispatcher.CheckAccess())
        { Dispatcher.InvokeAsync(End); return; }
        if (active > 0)
            active--;
        if (active > 0)
            return;
        showTimer.Stop();
        if (!IsShown)
            return;
        var remaining = MinVisible - (DateTime.UtcNow - shownAt);
        if (remaining <= TimeSpan.Zero)
            Hide();
        else
        { hideTimer.Interval = remaining; hideTimer.Start(); }
    }

    private void Report(JobProgress value)
    {
        if (!Dispatcher.CheckAccess())
        { Dispatcher.InvokeAsync(() => Report(value)); return; }
        bool next = value.Total > 1 && value.Processed > 0 && value.Phase != "queued";
        // Batches show real progress; a single job just keeps moving.
        determinate = next;
        progress = next ? (double)value.Processed / value.Total : 0;
        if (IsShown)
            Paint();
    }

    private void Show()
    {
        IsShown = true;
        shownAt = DateTime.UtcNow;
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        Paint();
    }

    private void Hide()
    {
        IsShown = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            if (IsShown)
                return;
            Visibility = Visibility.Collapsed;
            slide.BeginAnimation(TranslateTransform.XProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Paint()
    {
        double width = ActualWidth;
        if (width <= 0)
            return;
        if (determinate)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = 0;
            fill.Width = Math.Max(0, width * Math.Clamp(progress, 0, 1));
            return;
        }
        double bar = Math.Max(60, width * .28);
        fill.Width = bar;
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-bar, width, TimeSpan.FromMilliseconds(1150)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    public sealed class Session : IProgress<JobProgress>, IDisposable
    {
        private readonly BusyLine owner;
        private int ended;

        internal Session(BusyLine owner) => this.owner = owner;

        public void Report(JobProgress value)
        {
            if (Volatile.Read(ref ended) == 0)
                owner.Report(value);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref ended, 1) == 0)
                owner.End();
        }
    }
}
