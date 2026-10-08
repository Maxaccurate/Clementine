using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZestDrop;

// One watched folder: new files of this kind are processed with this action, and the results go to a sub-folder
// of the watched one, so they are never picked up again.
internal sealed record WatchRule(string Folder, string Kind, string Action);

internal sealed class WatchFolders : IDisposable
{
    private const string Key = "watchRules";
    private readonly Action<ConversionJob> enqueue;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    public WatchFolders(Action<ConversionJob> enqueue) => this.enqueue = enqueue;

    public static string OutputName => L.T("ZestDrop 输出");

    public static List<WatchRule> Load()
    {
        try
        { return JsonSerializer.Deserialize<List<WatchRule>>(Settings.GetString(Key) ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    public static void Save(List<WatchRule> rules) => Settings.Set(Key, JsonSerializer.Serialize(rules));

    public void Reload()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
        watchers.Clear();
        foreach (var rule in Load().Where(r => Directory.Exists(r.Folder)))
        {
            var watcher = new FileSystemWatcher(rule.Folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
            watcher.Created += (_, e) => _ = Guarded(rule, e.FullPath);
            watcher.Renamed += (_, e) => _ = Guarded(rule, e.FullPath);
            watchers.Add(watcher);
        }
    }

    private async Task Guarded(WatchRule rule, string path)
    {
        try
        { await Handle(rule, path); }
        catch (Exception ex) { Journal.Write("WatchFailed", new { rule.Folder, ex.Message }); }
    }

    private async Task Handle(WatchRule rule, string path)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith(".zestdrop-") || name.StartsWith("~$") || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            return;
        if (Directory.Exists(path) || Catalog.Category(path) != rule.Kind)
            return;
        lock (seen)
            if (!seen.Add(path))
                return;
        // A file that is still being copied or downloaded keeps growing or stays locked; wait until it settles.
        long last = -1;
        for (int attempt = 0; attempt < 600; attempt++)
        {
            await Task.Delay(1500).ConfigureAwait(false);
            try
            {
                if (!File.Exists(path))
                    return;
                long size = new FileInfo(path).Length;
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (size == last && size > 0)
                    break;
                last = size;
            }
            catch (IOException) { last = -1; }
        }
        string output = Path.Combine(rule.Folder, OutputName);
        try
        { Directory.CreateDirectory(output); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        // Conversions have no settings to remember; tools start from their defaults and then the settings used last.
        Dictionary<string, string> values = rule.Action.StartsWith("convert:") ? [] : Catalog.Definition(rule.Action, rule.Kind).Fields?.ToDictionary(f => f.Name, f => f.Default) ?? [];
        if (Presets.Last(rule.Action) is { } remembered)
            foreach (var (field, value) in remembered)
                if (values.ContainsKey(field))
                    values[field] = value;
        values["_outputDir"] = output;
        enqueue(new ConversionJob([path], rule.Action, values));
    }

    // What a rule can do, by kind of file: formats to convert to, and compressing with the settings last used.
    public static List<(string Action, string Label)> Actions(string kind)
    {
        string[] targets = kind switch { "image" => ["jpg", "png", "webp"], "video" => ["mp4", "webm", "gif"], _ => ["mp3", "m4a", "wav", "flac"] };
        var actions = targets.Select(target => ("convert:" + target, L.T("转换为 ") + target.ToUpperInvariant())).ToList();
        actions.Add(("compress", L.T("压缩（沿用上次的设置）")));
        return actions;
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
            watcher.Dispose();
        watchers.Clear();
    }
}
