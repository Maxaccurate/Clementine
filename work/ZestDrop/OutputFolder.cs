using System.IO;

namespace ZestDrop;

// Where results are written: next to the source file (the default) or in one folder the user picked in the tray menu.
internal static class OutputFolder
{
    private const string Key = "outputFolder";

    public static string? Current
    {
        get
        {
            string? chosen = Settings.GetString(Key);
            return !string.IsNullOrWhiteSpace(chosen) && Directory.Exists(chosen) ? chosen : null;
        }
    }

    public static void Choose(string? folder) => Settings.Set(Key, folder ?? "");

    // A job can carry its own folder (watch folders write to a sub-folder so their results are never picked up again).
    public static string? For(ConversionJob job) =>
        job.Parameters != null && job.Parameters.TryGetValue("_outputDir", out string? own) && Directory.Exists(own) ? own : Current;
}
