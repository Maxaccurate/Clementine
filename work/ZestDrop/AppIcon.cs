using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZestDrop;

internal static class AppIcon
{
    private static readonly Lazy<ImageSource> windowIcon = new(() =>
    {
        using var stream = Open();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[^1];
        frame.Freeze();
        return frame;
    });

    private static Stream Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("ZestDrop.AppIcon") ?? throw new InvalidOperationException("The application icon resource is missing.");
    public static ImageSource Window => windowIcon.Value;

    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Open();
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}
