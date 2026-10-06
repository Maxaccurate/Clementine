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
        Closed += (_, _) => L.Changed -= Rebuild;
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

        var hide = new CheckBox { Content = L.T("启动时不再显示此窗口"), IsChecked = Settings.GetBool(HideAtStartKey, false), Margin = new Thickness(0, 18, 0, 0), Foreground = UiTheme.Muted };
        hide.Click += (_, _) => Settings.Set(HideAtStartKey, hide.IsChecked == true);
        panel.Children.Add(hide);

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
            var button = new Button { Content = name, Margin = new Thickness(0, 0, 8, 0), FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, BorderBrush = current ? UiTheme.Accent : new SolidColorBrush(Color.FromRgb(224, 226, 229)) };
            button.Click += (_, _) => L.Language = code;
            languages.Children.Add(button);
        }
        footer.Children.Add(languages);
        panel.Children.Add(footer);
        return panel;
    }

    private static UIElement Step(int number, string title, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = new SolidColorBrush(Color.FromRgb(255, 240, 230)), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
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
