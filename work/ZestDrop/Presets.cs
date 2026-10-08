using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ZestDrop;

// Remembers each tool's settings: the ones used last time open again by themselves, and named presets can be saved and reused.
internal static class Presets
{
    // Tools whose settings make sense on other files. Regions, times and passwords belong to one file and are never stored.
    private static readonly HashSet<string> Tools = ["compress", "editImage", "frameImage", "rotateImage", "rotateVideo", "resizeImage", "watermark", "makeIcon", "ocrImage", "ocrPDF", "videoToGif",
        "videoSettings", "videoEffects", "subtitlesAudio", "audioEffects", "normalizeAudio", "audioChannels", "changeVideoSpeed", "createCollage", "createAnimation", "packArchive", "pdfNumbers",
        "extractPdfImages", "ringtone"];
    private static readonly HashSet<string> Skipped = ["start", "end", "time", "frame", "times", "password", "newPassword", "pageNumber", "imageFrame", "angle"];

    public static bool Supported(string id) => Tools.Contains(id);
    public static bool Keeps(string field) => !Skipped.Contains(field);

    private sealed class Entry
    {
        public Dictionary<string, string>? Last { get; set; }
        public Dictionary<string, Dictionary<string, string>> Named { get; set; } = [];
    }

    private static Entry Read(string id)
    {
        try
        { return JsonSerializer.Deserialize<Entry>(Settings.GetString("presets:" + id) ?? "") ?? new Entry(); }
        catch (JsonException) { return new Entry(); }
    }

    private static void Write(string id, Entry entry) => Settings.Set("presets:" + id, JsonSerializer.Serialize(entry));

    public static Dictionary<string, string>? Last(string id) => Read(id).Last;
    public static void SaveLast(string id, Dictionary<string, string> values) { var entry = Read(id); entry.Last = values; Write(id, entry); }
    public static List<string> Names(string id) => Read(id).Named.Keys.OrderBy(name => name).ToList();
    public static Dictionary<string, string>? Named(string id, string name) => Read(id).Named.GetValueOrDefault(name);
    public static void SaveNamed(string id, string name, Dictionary<string, string> values) { var entry = Read(id); entry.Named[name] = values; Write(id, entry); }
    public static void Delete(string id, string name) { var entry = Read(id); entry.Named.Remove(name); Write(id, entry); }
}
