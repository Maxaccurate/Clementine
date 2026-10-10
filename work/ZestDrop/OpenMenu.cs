using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZestDrop;

// A small menu at the pointer listing what can be done with some files. It is used when files arrive without a drag:
// from Explorer's right-click entry or from the clipboard.
internal static class OpenMenu
{
    public static void ShowList(string[] paths, List<Operation> options, Action<string[], Operation> submit)
    {
        var menu = new ContextMenu { StaysOpen = false, FontFamily = UiTheme.Font, FontSize = 13 };
        foreach (var option in options)
            menu.Items.Add(Item(paths, option, submit));
        Open(menu);
    }

    private static MenuItem Item(string[] paths, Operation option, Action<string[], Operation> submit)
    {
        var item = new MenuItem { Header = (option.Recent ? "↻ " : "") + option.Label, Foreground = UiTheme.Ink };
        item.Click += (_, _) => submit(paths, option);
        return item;
    }

    public static void Show(string[] paths, Action<string[], Operation> submit)
    {
        var menu = new ContextMenu { StaysOpen = false, FontFamily = UiTheme.Font, FontSize = 13 };
        var formats = Catalog.Options(paths, false);
        var tools = Catalog.Options(paths, true);
        if (formats.Count > 0 && !Catalog.PackingOnly(paths))
        {
            var convert = new MenuItem { Header = L.T("转换为") };
            foreach (var option in formats)
                convert.Items.Add(Item(paths, option, submit));
            menu.Items.Add(convert);
        }
        foreach (var option in tools)
            menu.Items.Add(Item(paths, option, submit));
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = L.T("无可用操作"), IsEnabled = false });
        Open(menu);
    }

    private static void Open(ContextMenu menu)
    {
        UiTheme.Refresh();
        menu.Background = UiTheme.Surface;
        menu.Foreground = UiTheme.Ink;
        menu.BorderBrush = UiTheme.Line;
        foreach (var item in menu.Items.OfType<MenuItem>())
        { item.Foreground = UiTheme.Ink; foreach (var sub in item.Items.OfType<MenuItem>()) sub.Foreground = UiTheme.Ink; }
        // The menu needs an active window to close properly when the user clicks elsewhere, so a one-pixel invisible one holds it.
        var anchor = new Window { Width = 1, Height = 1, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true };
        anchor.Show();
        Native.GetCursorPos(out var point);
        Native.SetWindowPos(new WindowInteropHelper(anchor).Handle, new IntPtr(-1), point.X, point.Y, 1, 1, 0x0040);
        anchor.Activate();
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Closed += (_, _) => anchor.Close();
        menu.IsOpen = true;
    }
}
