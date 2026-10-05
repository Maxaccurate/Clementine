using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ZestDrop;

// The desktop progress stack for background jobs. Every job gets its own card, listed top to bottom (oldest first).
// With two or more jobs a header appears: collapse piles the cards into one, expand lists them all, and
// "hide all" tucks the stack away while work continues. Cards can also be hidden one at a time.
internal sealed class TaskIndicatorWindow : Window
{
    private readonly bool headless;
    private readonly List<JobCard> cards = [];
    private readonly StackPanel list = new();
    private readonly StackPanel pile = new();
    private readonly Border header;
    // Each part ("进行中 1") wraps as a whole, so the summary never breaks mid-phrase.
    private readonly WrapPanel headerParts = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
    private string headerSummary = "";
    private readonly Button toggleButton, showAllButton, hideAllButton;
    private readonly ScrollViewer scroller;
    private System.Drawing.Rectangle? area;
    private bool expanded = true;

    public event Action<JobCard>? PauseRequested, ResumeRequested, CancelRequested;
    // "Hide all" stays in effect for new jobs until the stack is empty or the user shows it again.
    public bool AllHidden { get; private set; }
    public bool Expanded => expanded;
    public IReadOnlyList<JobCard> Cards => cards;
    public IEnumerable<JobCard> VisibleCards => list.Children.OfType<JobCard>().Where(card => card.Visibility == Visibility.Visible);
    public bool HeaderVisible => header.Visibility == Visibility.Visible;
    public string HeaderText => headerSummary;
    public bool ShouldShow { get; private set; }

    public TaskIndicatorWindow(bool headless = false)
    {
        this.headless = headless;
        Title = L.T("ZestDrop 处理进度");
        Icon = AppIcon.Window;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Resources.Add(typeof(Button), XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#24272B"/>
          <Setter Property="BorderBrush" Value="#E0E2E5"/><Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="12,4"/><Setter Property="MinHeight" Value="28"/><Setter Property="FontSize" Value="12"/><Setter Property="Cursor" Value="Hand"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Border x:Name="surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="surface" Property="BorderBrush" Value="#A9AFB8"/><Setter TargetName="surface" Property="Background" Value="#F6F7F8"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="surface" Property="Opacity" Value="0.45"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """));

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        toggleButton = HeaderButton(L.T("收起"), L.T("把卡片叠成一摞，只显示最新的任务"), () => SetExpanded(!expanded), actions);
        showAllButton = HeaderButton(L.T("显示全部"), L.T("重新显示被隐藏的卡片"), ShowAll, actions);
        hideAllButton = HeaderButton(L.T("全部隐藏"), L.T("隐藏进度，任务继续在后台运行；可从托盘菜单重新显示"), HideAll, actions);
        var bar = new DockPanel();
        DockPanel.SetDock(actions, Dock.Right);
        bar.Children.Add(actions);
        bar.Children.Add(headerParts);
        header = new Border
        {
            Child = bar, Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 6, 8, 6), Margin = new Thickness(10, 10, 10, 5),
            BorderBrush = new SolidColorBrush(Color.FromRgb(230, 232, 235)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 1, Opacity = .1 }
        };
        // Thin edges under the top card when the stack is collapsed, so it reads as a pile.
        for (int i = 0; i < 2; i++)
            pile.Children.Add(new Border { Height = 8, Margin = new Thickness(22 + i * 12, i == 0 ? -5 : -1, 22 + i * 12, 0), Background = new SolidColorBrush(i == 0 ? Color.FromRgb(250, 250, 251) : Color.FromRgb(244, 245, 247)), CornerRadius = new CornerRadius(0, 0, 9, 9), BorderBrush = new SolidColorBrush(Color.FromRgb(214, 218, 223)), BorderThickness = new Thickness(1, 0, 1, 1) });
        scroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(header);
        root.Children.Add(scroller);
        Content = root;
        SizeChanged += (_, _) => Place();
        Refresh();
    }

    private static Button HeaderButton(string text, string tip, System.Action click, Panel parent)
    {
        var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2), MinHeight = 26, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        button.Click += (_, _) => click();
        parent.Children.Add(button);
        return button;
    }

    public JobCard Add(ConversionJob job)
    {
        var card = new JobCard(job);
        card.PauseRequested += c => PauseRequested?.Invoke(c);
        card.ResumeRequested += c => ResumeRequested?.Invoke(c);
        card.CancelRequested += c => CancelRequested?.Invoke(c);
        card.HideRequested += HideCard;
        cards.Add(card);
        Refresh();
        return card;
    }

    // Shows the result for a few seconds, then removes the card. Returns false when the card was hidden,
    // so the caller can announce the result another way (the tray notification).
    public bool Finish(JobCard card, string title, string detail)
    {
        card.Complete(title, detail);
        bool shown = !AllHidden && !card.UserHidden;
        if (!shown)
            Remove(card);
        else
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); Remove(card); };
            timer.Start();
            Refresh();
        }
        return shown;
    }

    public void Remove(JobCard card)
    {
        if (!cards.Remove(card))
            return;
        if (cards.Count == 0)
            AllHidden = false;
        Refresh();
    }

    public void Update() => Refresh();

    public void Relabel()
    {
        Title = L.T("ZestDrop 处理进度");
        foreach (var card in cards)
            card.Relabel();
        Refresh();
    }

    public void HideCard(JobCard card)
    {
        card.UserHidden = true;
        if (card.State == JobState.Done)
            Remove(card);
        else
            Refresh();
    }

    public void HideAll()
    {
        AllHidden = true;
        Refresh();
    }

    public void ShowAll()
    {
        AllHidden = false;
        foreach (var card in cards)
            card.UserHidden = false;
        Refresh();
    }

    public void SetExpanded(bool value)
    {
        expanded = value;
        Refresh();
    }

    private void Refresh()
    {
        var shown = AllHidden ? [] : cards.Where(card => !card.UserHidden).ToList();
        // When piled, the card on top is the newest job that is actually running, then a paused one, then the newest.
        var top = shown.LastOrDefault(card => card.State == JobState.Running && !card.Paused) ?? shown.LastOrDefault(card => card.State == JobState.Running) ?? shown.LastOrDefault();
        bool piled = !expanded && shown.Count > 1;
        list.Children.Clear();
        foreach (var card in cards)
        {
            if (card.Parent is Panel old && old != list)
                old.Children.Remove(card);
            list.Children.Add(card);
            card.Visibility = shown.Contains(card) && (!piled || card == top) ? Visibility.Visible : Visibility.Collapsed;
            Panel.SetZIndex(card, 1);
            if (piled && card == top)
            {
                list.Children.Add(pile);
                for (int i = 0; i < pile.Children.Count; i++)
                    pile.Children[i].Visibility = i < shown.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        int active = cards.Count(card => card.State != JobState.Done);
        header.Visibility = cards.Count >= 2 && shown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        int running = cards.Count(card => card.State == JobState.Running && !card.Paused);
        int paused = cards.Count(card => card.Paused);
        int waiting = cards.Count(card => card.State == JobState.Waiting);
        int hidden = cards.Count(card => card.UserHidden && card.State != JobState.Done);
        var parts = new List<string> { L.F("{0} 个任务", active) };
        if (running > 0) parts.Add(L.F("进行中 {0}", running));
        if (paused > 0) parts.Add(L.F("已暂停 {0}", paused));
        if (waiting > 0) parts.Add(L.F("等待 {0}", waiting));
        if (hidden > 0) parts.Add(L.F("已隐藏 {0}", hidden));
        headerSummary = string.Join(" · ", parts);
        headerParts.Children.Clear();
        for (int i = 0; i < parts.Count; i++)
            headerParts.Children.Add(new TextBlock { Text = parts[i] + (i < parts.Count - 1 ? " · " : ""), FontSize = 12, Foreground = UiTheme.Muted });
        headerParts.ToolTip = headerSummary;
        toggleButton.Content = expanded ? L.T("收起") : L.T("展开");
        toggleButton.ToolTip = expanded ? L.T("把卡片叠成一摞，只显示最新的任务") : L.T("从上到下列出所有任务");
        toggleButton.Visibility = shown.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        showAllButton.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        showAllButton.Content = L.T("显示全部");
        showAllButton.ToolTip = L.T("重新显示被隐藏的卡片");
        hideAllButton.Content = L.T("全部隐藏");
        hideAllButton.ToolTip = L.T("隐藏进度，任务继续在后台运行；可从托盘菜单重新显示");

        ShouldShow = shown.Count > 0;
        if (headless)
            return;
        if (ShouldShow)
        {
            if (!IsVisible)
            {
                area = null; // pick the screen under the cursor each time the stack reappears
                Show();
            }
            Place();
        }
        else if (IsVisible)
            Hide();
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !IsVisible)
            return;
        if (area == null)
        {
            Native.GetCursorPos(out var cursor);
            area = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        }
        var work = area.Value;
        double scale = Native.GetDpiForWindow(handle) / 96.0;
        // Long stacks scroll instead of running off the screen.
        scroller.MaxHeight = Math.Max(160, work.Height / scale - 80);
        int width = (int)Math.Round(ActualWidth * scale), height = (int)Math.Round(ActualHeight * scale), gap = (int)Math.Round(8 * scale);
        if (width == 0 || height == 0)
            return;
        Native.SetWindowPos(handle, new IntPtr(-1), work.Right - width - gap, work.Bottom - height - gap, width, height, 0x0010 | 0x0040);
    }
}
