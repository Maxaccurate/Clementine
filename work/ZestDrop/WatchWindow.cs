using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace ZestDrop;

// Lists the watched folders and lets the user add or remove one.
internal sealed class WatchWindow : Window
{
    private readonly List<WatchRule> rules = WatchFolders.Load();
    private readonly ListBox list = new() { MinHeight = 120, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBox folder = new() { Padding = new Thickness(8), IsReadOnly = true };
    private readonly ComboBox kind = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly ComboBox action = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Action changed;

    public WatchWindow(Action changed)
    {
        this.changed = changed;
        Title = L.T("监视文件夹");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = UiTheme.Font;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 251));
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = L.F("放进这些文件夹的新文件会自动处理，结果保存在其中的“{0}”文件夹里。", WatchFolders.OutputName), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(list);
        var remove = new Button { Content = L.T("移除选中的文件夹"), Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
        remove.Click += (_, _) => { if (list.SelectedIndex >= 0) { rules.RemoveAt(list.SelectedIndex); Commit(); } };
        stack.Children.Add(remove);
        stack.Children.Add(new TextBlock { Text = L.T("添加文件夹"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var pick = new DockPanel();
        var browse = new Button { Content = L.T("选择…"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Click += (_, _) => { using var dialog = new Forms.FolderBrowserDialog(); if (dialog.ShowDialog() == Forms.DialogResult.OK) folder.Text = dialog.SelectedPath; };
        pick.Children.Add(browse);
        pick.Children.Add(folder);
        stack.Children.Add(pick);
        foreach (var (id, label) in new[] { ("image", L.T("图片")), ("video", L.T("视频")), ("audio", L.T("音频")) })
            kind.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        kind.SelectionChanged += (_, _) => FillActions();
        stack.Children.Add(kind);
        stack.Children.Add(action);
        var add = new Button { Content = L.T("添加"), Padding = new Thickness(18, 8, 18, 8), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(folder.Text) || kind.SelectedItem is not ComboBoxItem { Tag: string type } || action.SelectedItem is not ComboBoxItem { Tag: string chosen })
                return;
            rules.Add(new WatchRule(folder.Text, type, chosen));
            folder.Text = "";
            Commit();
        };
        stack.Children.Add(add);
        Content = stack;
        kind.SelectedIndex = 0;
        Refresh();
    }

    private void FillActions()
    {
        action.Items.Clear();
        if (kind.SelectedItem is ComboBoxItem { Tag: string type })
            foreach (var (id, label) in WatchFolders.Actions(type))
                action.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        action.SelectedIndex = 0;
    }

    private void Commit()
    {
        WatchFolders.Save(rules);
        Refresh();
        changed();
    }

    private void Refresh()
    {
        list.Items.Clear();
        foreach (var rule in rules)
        {
            string label = WatchFolders.Actions(rule.Kind).FirstOrDefault(a => a.Action == rule.Action).Label ?? rule.Action;
            list.Items.Add(rule.Folder + "  →  " + label);
        }
    }
}
