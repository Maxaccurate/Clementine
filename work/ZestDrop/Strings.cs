using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZestDrop;

// Interface language. Source strings are written in Chinese and double as lookup keys:
// L.T("压缩") is "Compress" in English. L.F formats keys with {0}-style placeholders.
internal static partial class L
{
    private static string? language;

    public static event Action? Changed;

    // "zh" or "en". Defaults to Chinese on a Chinese Windows display language and English elsewhere.
    public static string Language
    {
        // ZESTDROP_LANG (set for worker processes) wins, so child processes such as the Office bridge match the app.
        get => language ??= Override() ?? Load() ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en");
        set
        {
            string next = value == "zh" ? "zh" : "en";
            if (next == Language)
                return;
            language = next;
            Settings.Set("language", next);
            Changed?.Invoke();
        }
    }

    public static bool English => Language == "en";

    public static string T(string chinese) => English && En.TryGetValue(chinese, out var english) ? english : chinese;

    public static string F(string chinese, params object?[] values) => string.Format(CultureInfo.InvariantCulture, T(chinese), values);

    // For tests and tools that must not touch the user's saved choice.
    public static void UseForSession(string value) => language = value == "zh" ? "zh" : "en";

    private static string? Override() => Environment.GetEnvironmentVariable("ZESTDROP_LANG") is "zh" or "en" ? Environment.GetEnvironmentVariable("ZESTDROP_LANG") : null;

    private static string? Load() => Settings.GetString("language") is "zh" or "en" ? Settings.GetString("language") : null;

    public static IReadOnlyDictionary<string, string> Translations => En;
}
