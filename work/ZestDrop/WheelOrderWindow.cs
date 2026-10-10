using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ZestDrop;

// Lets people put the options they use most where they want them in the wheel. The order is kept per kind of file
// and per menu (formats or tools); options that are not in the saved order follow in their usual order.
internal sealed class WheelOrderWindow : Window
{
    private readonly ComboBox kind = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly ComboBox menu = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly ListBox list = new() { Height = 280, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock empty = new() { Foreground = UiTheme.Muted, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };

    public WheelOrderWindow()
    {
        UiTheme.Apply(this);
        Title = L.T("自定义轮盘");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var stack = new StackPanel { Margin = new Thickness(28, 4, 28, 24) };
        stack.Children.Add(new TextBlock { Text = L.T("自定义轮盘"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = L.T("选择文件类型和菜单，再调整选项的顺序。第一项在轮盘顶部，其余按顺时针排列。"), Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 16) });
        foreach (var (id, label) in new[] { ("image", L.T("图片")), ("video", L.T("视频")), ("audio", L.T("音频")), ("document", L.T("PDF 与文本")), ("archive", L.T("压缩包")) })
            kind.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        menu.Items.Add(new ComboBoxItem { Content = L.T("工具（Ctrl+Shift）"), Tag = true });
        menu.Items.Add(new ComboBoxItem { Content = L.T("格式（Shift）"), Tag = false });
        var pickers = new Grid();
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.Children.Add(Labelled(L.T("文件类型"), kind));
        var menuPicker = Labelled(L.T("菜单"), menu);
        Grid.SetColumn(menuPicker, 2);
        pickers.Children.Add(menuPicker);
        stack.Children.Add(pickers);
        stack.Children.Add(list);
        stack.Children.Add(empty);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        buttons.Children.Add(MakeButton(L.T("移到最前"), () => Move(-list.SelectedIndex)));
        buttons.Children.Add(MakeButton(L.T("上移"), () => Move(-1)));
        buttons.Children.Add(MakeButton(L.T("下移"), () => Move(1)));
        buttons.Children.Add(MakeButton(L.T("恢复默认顺序"), () => { Catalog.SaveOrder(Kind, Tools, null); Fill(); }));
        stack.Children.Add(buttons);
        var lastFirst = new CheckBox { Content = L.T("把上次使用的选项放在最前（带 ↻ 标记）"), IsChecked = Catalog.LastFirst };
        lastFirst.Click += (_, _) => Settings.Set(Catalog.LastFirstKey, lastFirst.IsChecked == true);
        stack.Children.Add(lastFirst);
        Content = UiTheme.Frame(this, stack, "ZestDrop");
        kind.SelectionChanged += (_, _) => Fill();
        menu.SelectionChanged += (_, _) => Fill();
        kind.SelectedIndex = 0;
        menu.SelectedIndex = 0;
    }

    private string Kind => (kind.SelectedItem as ComboBoxItem)?.Tag as string ?? "image";
    private bool Tools => (menu.SelectedItem as ComboBoxItem)?.Tag is true;

    private static StackPanel Labelled(string label, UIElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(control);
        return panel;
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6) };
        button.Click += (_, _) => click();
        return button;
    }

    private void Fill(int select = -1)
    {
        if (kind.SelectedItem == null || menu.SelectedItem == null)
            return;
        list.Items.Clear();
        foreach (var operation in Catalog.Sample(Kind, Tools))
            list.Items.Add(new ListBoxItem { Content = operation.Label, Tag = operation.Id });
        empty.Text = L.T("这里没有可调整的选项。");
        empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        list.SelectedIndex = Math.Min(select, list.Items.Count - 1);
    }

    // Moves the selected option and saves the whole order shown.
    private void Move(int by)
    {
        int index = list.SelectedIndex, target = index + by;
        if (index < 0 || target < 0 || target >= list.Items.Count || by == 0)
            return;
        var ids = list.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToList();
        string moved = ids[index];
        ids.RemoveAt(index);
        ids.Insert(target, moved);
        Catalog.SaveOrder(Kind, Tools, ids);
        Fill(target);
    }
}
