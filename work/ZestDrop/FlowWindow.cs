using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ZestDrop;

// Builds and lists flows. A step is a tool plus one of the presets saved from its window.
internal sealed class FlowWindow : Window
{
    private readonly List<Flow> flows = Flows.Load();
    private readonly List<FlowStep> steps = [];
    private readonly ListBox saved = new() { MinHeight = 80, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox stepList = new() { MinHeight = 90, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBox name = new() { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox kind = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox tool = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly ComboBox preset = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly Action changed;

    public FlowWindow(Action changed)
    {
        this.changed = changed;
        Title = L.T("流程");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = UiTheme.Font;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 251));
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = L.T("流程把几个工具连起来依次处理每个文件，会出现在对应文件的工具菜单里。每一步使用在工具窗口里“保存为预设”的设置。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(new TextBlock { Text = L.T("已有的流程"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(saved);
        var delete = new Button { Content = L.T("删除选中的流程"), Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
        delete.Click += (_, _) => { if (saved.SelectedIndex >= 0) { flows.RemoveAt(saved.SelectedIndex); Commit(); } };
        stack.Children.Add(delete);
        stack.Children.Add(new TextBlock { Text = L.T("新建流程"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(new TextBlock { Text = L.T("名称"), Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(name);
        foreach (var (id, label) in new[] { ("image", L.T("图片")), ("video", L.T("视频")), ("audio", L.T("音频")), ("document", "PDF") })
            kind.Items.Add(new ComboBoxItem { Content = label, Tag = id });
        kind.SelectionChanged += (_, _) => { steps.Clear(); FillTools(); RefreshSteps(); };
        stack.Children.Add(new TextBlock { Text = L.T("适用文件"), Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(kind);
        stack.Children.Add(new TextBlock { Text = L.T("添加一步"), Margin = new Thickness(0, 0, 0, 3) });
        tool.SelectionChanged += (_, _) => FillPresets();
        stack.Children.Add(tool);
        stack.Children.Add(preset);
        var buttons = new WrapPanel();
        buttons.Children.Add(MakeButton(L.T("加入这一步"), AddStep));
        buttons.Children.Add(MakeButton(L.T("移除选中的步骤"), () => { if (stepList.SelectedIndex >= 0) { steps.RemoveAt(stepList.SelectedIndex); RefreshSteps(); } }));
        buttons.Children.Add(MakeButton(L.T("上移"), () => Move(-1)));
        buttons.Children.Add(MakeButton(L.T("下移"), () => Move(1)));
        stack.Children.Add(buttons);
        stack.Children.Add(stepList);
        var save = new Button { Content = L.T("保存流程"), Padding = new Thickness(18, 8, 18, 8), HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || steps.Count == 0 || kind.SelectedItem is not ComboBoxItem { Tag: string type })
                return;
            flows.RemoveAll(f => f.Name == name.Text.Trim());
            flows.Add(new Flow(name.Text.Trim(), type, steps.ToList()));
            name.Text = "";
            steps.Clear();
            Commit();
        };
        stack.Children.Add(save);
        Content = stack;
        kind.SelectedIndex = 0;
        Commit(notify: false);
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    private string CurrentKind => (kind.SelectedItem as ComboBoxItem)?.Tag as string ?? "image";

    private void FillTools()
    {
        tool.Items.Clear();
        foreach (string id in Flows.StepTools(CurrentKind))
            tool.Items.Add(new ComboBoxItem { Content = Catalog.Definition(id, CurrentKind).Label, Tag = id });
        tool.SelectedIndex = tool.Items.Count > 0 ? 0 : -1;
    }

    private void FillPresets()
    {
        preset.Items.Clear();
        preset.Items.Add(new ComboBoxItem { Content = L.T("默认设置"), Tag = "" });
        if (tool.SelectedItem is ComboBoxItem { Tag: string id })
            foreach (string saved in Presets.Names(id))
                preset.Items.Add(new ComboBoxItem { Content = L.F("预设：{0}", saved), Tag = saved });
        preset.SelectedIndex = 0;
    }

    private void AddStep()
    {
        if (tool.SelectedItem is ComboBoxItem { Tag: string id } && preset.SelectedItem is ComboBoxItem { Tag: string chosen })
        { steps.Add(new FlowStep(id, chosen.Length == 0 ? null : chosen)); RefreshSteps(); }
    }

    private void Move(int by)
    {
        int index = stepList.SelectedIndex, target = index + by;
        if (index < 0 || target < 0 || target >= steps.Count)
            return;
        (steps[index], steps[target]) = (steps[target], steps[index]);
        RefreshSteps();
        stepList.SelectedIndex = target;
    }

    private void RefreshSteps()
    {
        stepList.Items.Clear();
        for (int i = 0; i < steps.Count; i++)
            stepList.Items.Add($"{i + 1}. {Catalog.Definition(steps[i].Action, CurrentKind).Label}" + (steps[i].Preset == null ? "" : $" · {steps[i].Preset}"));
    }

    private void Commit(bool notify = true)
    {
        Flows.Save(flows);
        saved.Items.Clear();
        foreach (var flow in flows)
            saved.Items.Add($"{flow.Name}  ({flow.Steps.Count})");
        RefreshSteps();
        if (notify)
            changed();
    }
}
