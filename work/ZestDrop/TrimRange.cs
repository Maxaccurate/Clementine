using System;

namespace ZestDrop;

internal static class TrimRange
{
    public static double Start(double value, double end, double duration) => Math.Clamp(value, 0, Math.Max(0, end - Gap(duration)));
    public static double End(double value, double start, double duration) => Math.Clamp(value, Math.Min(duration, start + Gap(duration)), duration);
    public static double Gap(double duration) => Math.Min(.05, Math.Max(0, duration));
}
