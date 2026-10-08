using System;
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
    public static void Show(string[] paths, Action<string[], Operation> submit)
    {
        var menu = new ContextMenu { StaysOpen = false, FontFamily = UiTheme.Font, FontSize = 13 };
        var formats = Catalog.Options(paths, false);
        var tools = Catalog.Options(paths, true);
        if (formats.Count > 0 && !Catalog.PackingOnly(paths))
        {
            var convert = new MenuItem { Header = L.T("转换为") };
            foreach (var option in formats)
            {
                var item = new MenuItem { Header = option.Label };
                item.Click += (_, _) => submit(paths, option);
                convert.Items.Add(item);
            }
            menu.Items.Add(convert);
        }
        foreach (var option in tools)
        {
            var item = new MenuItem { Header = option.Label };
            item.Click += (_, _) => submit(paths, option);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = L.T("无可用操作"), IsEnabled = false });
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
