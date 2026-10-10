using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZestDrop;

// ZestDrop has no main window, so this is the first thing a person sees after installing it: what it does and
// where it lives. It opens when ZestDrop starts, and again whenever ZestDrop is launched while already running.
internal sealed class WelcomeWindow : Window
{
    public const string HideAtStartKey = "hideWelcome";
    private CheckBox? startup;
    private TextBlock? startupMessage;
    private Button? startupSettings;

    public WelcomeWindow()
    {
        UiTheme.Apply(this);
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "ZestDrop";
        // Re-read every label if the language changes while the window is open (from the buttons below or the tray menu).
        L.Changed += Rebuild;
        StartupManager.Changed += RefreshStartup;
        Closed += (_, _) => L.Changed -= Rebuild;
        Closed += (_, _) => StartupManager.Changed -= RefreshStartup;
        Rebuild();
    }

    private void Rebuild() => Content = UiTheme.Frame(this, Body(), "ZestDrop");

    private UIElement Body()
    {
        var panel = new StackPanel { Margin = new Thickness(32, 4, 32, 26) };

        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(new Image { Source = AppIcon.Window, Width = 52, Height = 52, Margin = new Thickness(0, 0, 16, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = L.T("欢迎使用 ZestDrop"), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = UiTheme.Ink });
        titles.Children.Add(new TextBlock { Text = L.T("拖动，松手，转换。"), FontSize = 14, Foreground = UiTheme.Muted, Margin = new Thickness(0, 2, 0, 0) });
        heading.Children.Add(titles);
        panel.Children.Add(heading);

        var steps = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
        steps.Children.Add(Step(1, L.T("拖动文件"), L.T("在桌面或资源管理器中选中一个或多个文件，像移动文件一样开始拖动。")));
        steps.Children.Add(Step(2, L.T("按下 Shift"), L.T("拖动时按 Shift 打开格式菜单，按 Ctrl+Shift 打开工具菜单。菜单出现后可以松开按键。")));
        steps.Children.Add(Step(3, L.T("在选项上松手"), L.T("新文件会保存在原文件旁边，原文件不会被改动。")));
        panel.Children.Add(steps);

        var tray = new StackPanel();
        tray.Children.Add(new TextBlock { Text = L.T("ZestDrop 在通知区域（任务栏右侧时钟旁）运行，没有主窗口。右键托盘图标可暂停或取消任务、切换语言或退出。看不到图标？点击任务栏上的 ^ 箭头。"), TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Ink, LineHeight = 20 });
        tray.Children.Add(new TextBlock { Text = L.T("所有处理都在本机完成，不上传任何文件，也不联网。"), TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        var card = UiTheme.Card(tray, new Thickness(18, 14, 18, 14));
        card.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(card);
        panel.Children.Add(LayoutChoice());

        var hide = new CheckBox { Content = L.T("启动时不再显示此窗口"), IsChecked = Settings.GetBool(HideAtStartKey, false), Margin = new Thickness(0, 18, 0, 0), Foreground = UiTheme.Muted };
        hide.Click += (_, _) => Settings.Set(HideAtStartKey, hide.IsChecked == true);
        panel.Children.Add(hide);
        startup = new CheckBox { Content = L.T("开机自启"), IsEnabled = false, Margin = new Thickness(0, 6, 0, 0), ToolTip = L.T("登录 Windows 后在后台运行，不打开欢迎窗口。") };
        startupMessage = new TextBlock { Foreground = UiTheme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 3, 0, 0) };
        startupSettings = new Button { Content = L.T("Windows 启动应用设置"), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(6, 5, 0, 0) };
        startupSettings.Click += (_, _) => StartupManager.OpenWindowsSettings();
        startup.Click += async (_, _) =>
        {
            startup.IsEnabled = false;
            var status = await StartupManager.SetAsync(startup.IsChecked == true);
            UpdateStartup(status);
        };
        panel.Children.Add(startup); panel.Children.Add(startupMessage); panel.Children.Add(startupSettings);
        RefreshStartup();

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var close = new Button { Content = L.T("知道了"), Background = UiTheme.Accent, Foreground = Brushes.White, BorderBrush = UiTheme.Accent, Padding = new Thickness(26, 9, 26, 9), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        var languages = new StackPanel { Orientation = Orientation.Horizontal };
        // Language names are always shown in their own language so either one can be found.
        foreach (var (code, name) in new[] { ("en", "English"), ("zh", "简体中文") })
        {
            bool current = L.Language == code;
            var button = new Button { Content = name, Margin = new Thickness(0, 0, 8, 0), FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, BorderBrush = current ? UiTheme.Accent : UiTheme.Line };
            button.Click += (_, _) => L.Language = code;
            languages.Children.Add(button);
        }
        footer.Children.Add(languages);
        panel.Children.Add(footer);
        return panel;
    }
    private async void RefreshStartup() => UpdateStartup(await StartupManager.GetAsync());
    private void UpdateStartup(StartupStatus status)
    {
        if (startup == null || startupMessage == null || startupSettings == null) return;
        startup.IsChecked = status.Enabled; startup.IsEnabled = status.CanChange;
        startupMessage.Text = status.Message ?? L.T("登录 Windows 后在后台运行，不打开欢迎窗口。");
        startupSettings.Visibility = status.Message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // The two ways a wheel with many tools can look; the same setting as the tray menu's layout item.
    private UIElement LayoutChoice()
    {
        var section = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        section.Children.Add(new TextBlock { Text = L.T("工具较多时的轮盘"), FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = UiTheme.Ink });
        section.Children.Add(new TextBlock { Text = L.T("单个图片或视频的工具较多时适用；随时可在托盘菜单中更改。"), TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, Margin = new Thickness(0, 2, 0, 8) });
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition());
        choices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        choices.ColumnDefinitions.Add(new ColumnDefinition());
        var merged = LayoutCard(L.T("合并相关工具"), L.T("相关工具合在一个窗口里，一个轮盘就够。"), Picture(false), !Catalog.TwoWheels, "merged");
        var twoWheels = LayoutCard(L.T("两层轮盘"), L.T("先选类别，再在第二个轮盘里选工具。"), Picture(true), Catalog.TwoWheels, "twoWheels");
        Grid.SetColumn(twoWheels, 2);
        choices.Children.Add(merged);
        choices.Children.Add(twoWheels);
        section.Children.Add(choices);
        return section;
    }

    private Button LayoutCard(string title, string text, UIElement picture, bool chosen, string value)
    {
        var row = new DockPanel();
        DockPanel.SetDock(picture, Dock.Left);
        row.Children.Add(picture);
        var words = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = (chosen ? "✓ " : "") + title, FontWeight = FontWeights.SemiBold, Foreground = chosen ? UiTheme.Accent : UiTheme.Ink });
        words.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(words);
        var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10, 12, 10) };
        if (chosen)
        {
            button.Background = UiTheme.Tint;
            button.BorderBrush = UiTheme.Accent;
        }
        button.Click += (_, _) => { Settings.Set(Catalog.LayoutKey, value); Rebuild(); };
        return button;
    }

    // A small drawing of the layout: one wheel, or a category wheel leading to a second wheel.
    private static UIElement Picture(bool twoWheels)
    {
        var canvas = new System.Windows.Controls.Canvas { Width = twoWheels ? 84 : 52, Height = 52 };
        AddWheel(canvas, 26, 26, 25, twoWheels ? 5 : 8, twoWheels ? 1 : -1);
        if (twoWheels)
        {
            canvas.Children.Add(new TextBlock { Text = "›", FontSize = 16, Foreground = UiTheme.Muted, Margin = new Thickness(53, 13, 0, 0) });
            AddWheel(canvas, 72, 26, 12, 4, -1);
        }
        return canvas;
    }

    private static void AddWheel(System.Windows.Controls.Canvas canvas, double cx, double cy, double radius, int slices, int highlighted)
    {
        double inner = radius * 0.42, gap = 0.09;
        for (int i = 0; i < slices; i++)
        {
            double a0 = 2 * Math.PI * i / slices - Math.PI / 2 + gap, a1 = 2 * Math.PI * (i + 1) / slices - Math.PI / 2 - gap;
            Point P(double r, double a) => new(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            var figure = new PathFigure { StartPoint = P(inner, a0), IsClosed = true };
            figure.Segments.Add(new LineSegment(P(radius, a0), true));
            figure.Segments.Add(new ArcSegment(P(radius, a1), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(P(inner, a1), true));
            figure.Segments.Add(new ArcSegment(P(inner, a0), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = new PathGeometry([figure]),
                Fill = i == highlighted ? UiTheme.Accent : UiTheme.Line,
            });
        }
    }

    private static UIElement Step(int number, string title, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = UiTheme.Tint, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        badge.Child = new TextBlock { Text = number.ToString(), FontWeight = FontWeights.Bold, Foreground = UiTheme.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(badge);
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = UiTheme.Ink });
        words.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = UiTheme.Muted, Margin = new Thickness(0, 2, 0, 0), LineHeight = 20 });
        Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }
}
