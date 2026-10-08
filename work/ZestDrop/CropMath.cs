using System;
using System.Globalization;

namespace ZestDrop;

/// <summary>A crop rectangle in source pixels.</summary>
internal readonly record struct CropRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>A width:height ratio written the way the backend reads it, such as "4:3" or "2.35:1".</summary>
internal readonly record struct CropRatio(double A, double B)
{
    public double Value => A / B;

    public static CropRatio? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "free")
            return null;
        string[] parts = text.Split(':');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b)
            || !double.IsFinite(a) || !double.IsFinite(b) || a <= 0 || b <= 0)
            return null;
        return new CropRatio(a, b);
    }
}

internal enum CropHandle { None, Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Which side of the crop a typed value is allowed to dictate when a ratio is locked.</summary>
internal enum CropKeep { Nothing, Width, Height }

/// <summary>
/// Geometry of the crop frame, kept free of UI types so it can be tested directly.
/// Every result lies inside the image. With a ratio, every result has that ratio to within whole-pixel rounding,
/// which is exactly what the backend's fit_ratio leaves alone, so the size shown is the size saved.
/// </summary>
internal static class CropMath
{
    /// <summary>The smallest crop the frame allows, so it never collapses to something that cannot be grabbed.</summary>
    public static int MinSize(int width, int height) => Math.Max(1, Math.Min(16, Math.Min(width, height) / 4));

    /// <summary>True when one side is the other side times the ratio, give or take rounding to a whole pixel.</summary>
    public static bool Conforms(int width, int height, CropRatio ratio) =>
        Math.Abs(height - width * ratio.B / ratio.A) <= .5 || Math.Abs(width - height * ratio.A / ratio.B) <= .5;

    /// <summary>The size whose given side is <paramref name="primary"/> and whose other side follows the ratio.</summary>
    private static (int Width, int Height) Pair(int primary, bool primaryIsWidth, CropRatio ratio)
    {
        double ideal = primaryIsWidth ? primary * ratio.B / ratio.A : primary * ratio.A / ratio.B;
        int low = (int)Math.Floor(ideal), high = low + 1;
        int first = ideal - low <= high - ideal ? low : high, second = first == low ? high : low;
        foreach (int other in new[] { first, second })
        {
            var pair = primaryIsWidth ? (primary, other) : (other, primary);
            if (other >= 1 && Conforms(pair.Item1, pair.Item2, ratio))
                return pair;
        }
        int fallback = Math.Max(1, first);
        return primaryIsWidth ? (primary, fallback) : (fallback, primary);
    }

    /// <summary>
    /// Turns a wanted size into the nearest whole-pixel size that has the ratio and does not exceed the limits.
    /// The primary side is rounded; the other follows it.
    /// </summary>
    private static (int Width, int Height) Snap(double width, double height, CropRatio ratio, int maxWidth, int maxHeight, bool widthPrimary)
    {
        int start = Math.Max(1, (int)Math.Round(widthPrimary ? width : height));
        for (int primary = start; primary >= 1; primary--)
        {
            var (w, h) = Pair(primary, widthPrimary, ratio);
            if (w <= maxWidth && h <= maxHeight)
                return (w, h);
        }
        return (1, 1);
    }

    /// <summary>Moves the crop by a distance in source pixels, keeping its size and staying inside the image.</summary>
    public static CropRect Move(CropRect start, double dx, double dy, int width, int height) => start with
    {
        X = (int)Math.Round(Math.Clamp(start.X + dx, 0, Math.Max(0, width - start.Width))),
        Y = (int)Math.Round(Math.Clamp(start.Y + dy, 0, Math.Max(0, height - start.Height)))
    };

    /// <summary>
    /// Resizes the crop by dragging a handle by (dx, dy) source pixels. The side or corner opposite the handle stays put.
    /// With a ratio, a corner scales the frame toward the pointer and a side scales it about the middle of the other axis.
    /// </summary>
    public static CropRect Resize(CropRect start, CropHandle handle, double dx, double dy, int width, int height, CropRatio? ratio, int min)
    {
        bool left = handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft;
        bool right = handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight;
        bool top = handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight;
        bool bottom = handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight;
        if (!(left || right || top || bottom))
            return start;

        if (ratio is not { } r)
        {
            double l = start.X, t = start.Y, rt = start.Right, b = start.Bottom;
            if (left) l = Math.Clamp(start.X + dx, 0, Math.Max(0, rt - min));
            if (right) rt = Math.Clamp(start.Right + dx, Math.Min(l + min, width), width);
            if (top) t = Math.Clamp(start.Y + dy, 0, Math.Max(0, b - min));
            if (bottom) b = Math.Clamp(start.Bottom + dy, Math.Min(t + min, height), height);
            int x = (int)Math.Round(l), y = (int)Math.Round(t);
            return new CropRect(x, y, (int)Math.Round(rt) - x, (int)Math.Round(b) - y);
        }

        double q = r.Value;
        if ((left || right) && (top || bottom))
        {
            // The opposite corner stays fixed; the frame takes the size nearest the pointer that has the ratio.
            int ax = right ? start.X : start.Right, ay = bottom ? start.Y : start.Bottom;
            double px = (right ? start.Right : start.X) + dx, py = (bottom ? start.Bottom : start.Y) + dy;
            double wanted = right ? px - ax : ax - px, tall = bottom ? py - ay : ay - py;
            double h = (wanted * q + tall) / (q * q + 1);
            int widthRoom = right ? width - ax : ax, heightRoom = bottom ? height - ay : ay;
            double hMax = Math.Min(heightRoom, widthRoom / q), hMin = Math.Min(Math.Max(min, min / q), hMax);
            h = Math.Clamp(h, hMin, hMax);
            var (w, hh) = Snap(h * q, h, r, widthRoom, heightRoom, q >= 1);
            return new CropRect(right ? ax : ax - w, bottom ? ay : ay - hh, w, hh);
        }

        if (left || right)
        {
            int ax = right ? start.X : start.Right;
            int widthRoom = right ? width - ax : ax;
            double wMax = Math.Min(widthRoom, height * q), wMin = Math.Min(Math.Max(min, min * q), wMax);
            double wanted = Math.Clamp(right ? start.Right + dx - ax : ax - (start.X + dx), wMin, wMax);
            var (w, h) = Snap(wanted, wanted / q, r, widthRoom, height, true);
            double centre = start.Y + start.Height / 2.0;
            int y = (int)Math.Round(Math.Clamp(centre - h / 2.0, 0, Math.Max(0, height - h)));
            return new CropRect(right ? ax : ax - w, y, w, h);
        }
        else
        {
            int ay = bottom ? start.Y : start.Bottom;
            int heightRoom = bottom ? height - ay : ay;
            double hMax = Math.Min(heightRoom, width / q), hMin = Math.Min(Math.Max(min, min / q), hMax);
            double wanted = Math.Clamp(bottom ? start.Bottom + dy - ay : ay - (start.Y + dy), hMin, hMax);
            var (w, h) = Snap(wanted * q, wanted, r, width, heightRoom, false);
            double centre = start.X + start.Width / 2.0;
            int x = (int)Math.Round(Math.Clamp(centre - w / 2.0, 0, Math.Max(0, width - w)));
            return new CropRect(x, bottom ? ay : ay - h, w, h);
        }
    }

    /// <summary>
    /// Brings any rectangle (typed values, a new ratio) to a valid crop: inside the image, not too small, and with the
    /// ratio. A typed width or height can dictate the other side; otherwise the frame shrinks about its own centre.
    /// </summary>
    public static CropRect Constrain(CropRect rect, int width, int height, CropRatio? ratio, int min, CropKeep keep = CropKeep.Nothing)
    {
        int w = Math.Clamp(rect.Width, Math.Min(min, width), width), h = Math.Clamp(rect.Height, Math.Min(min, height), height);
        int x = rect.X, y = rect.Y;
        if (ratio is { } r)
        {
            double q = r.Value, idealW, idealH;
            bool widthPrimary;
            switch (keep)
            {
                case CropKeep.Width:
                    idealW = w; idealH = w / q; widthPrimary = true;
                    break;
                case CropKeep.Height:
                    idealH = h; idealW = h * q; widthPrimary = false;
                    break;
                default:
                    if ((double)w / h > q) { idealH = h; idealW = h * q; }
                    else { idealW = w; idealH = w / q; }
                    widthPrimary = q >= 1;
                    break;
            }
            double fit = Math.Min(1, Math.Min(width / idealW, height / idealH));
            var (nw, nh) = Snap(idealW * fit, idealH * fit, r, width, height, widthPrimary);
            // A very wide or tall ratio can leave one side below the minimum; grow back to the smallest size that is not.
            double smallestHeight = Math.Max(min, min / q);
            if (nh < smallestHeight - .5 || nw < min - .5)
                (nw, nh) = Snap(smallestHeight * q, smallestHeight, r, width, height, false);
            if (keep == CropKeep.Nothing)
            { x += (w - nw) / 2; y += (h - nh) / 2; }
            w = nw; h = nh;
        }
        return new CropRect(Math.Clamp(x, 0, Math.Max(0, width - w)), Math.Clamp(y, 0, Math.Max(0, height - h)), w, h);
    }
}
