using System;

namespace ZestDrop;

internal static class RotationGeometry
{
    // Largest centred rectangle with the source aspect ratio inside the rotated source.
    public static (int Width, int Height) CropSize(int width, int height, double angle)
    {
        double radians = angle * Math.PI / 180, cos = Math.Abs(Math.Cos(radians)), sin = Math.Abs(Math.Sin(radians));
        double inset = Math.Min(2, Math.Min(width, height) / 8d);
        double scale = Math.Min((width - 2 * inset) / (width * cos + height * sin),
            (height - 2 * inset) / (width * sin + height * cos));
        return (Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }
}
