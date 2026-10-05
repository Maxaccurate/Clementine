using System;

namespace ZestDrop;

internal sealed record MediaSegment(double Start, double Length, double Rate = 1)
{
    public double End => Start + Length * Rate;
    public bool Contains(double sourceTime) => sourceTime >= Start - .001 && sourceTime <= End + .001;
    public double ToMedia(double sourceTime) => Math.Clamp((sourceTime - Start) / Math.Max(.001, Rate), 0, Length);
    public double ToSource(double mediaTime) => Start + Math.Clamp(mediaTime, 0, Length) * Rate;
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return seconds >= 3600 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
