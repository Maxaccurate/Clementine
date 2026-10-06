using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZestDrop;

// Small per-user settings file (%LOCALAPPDATA%\ZestDrop\settings.json). Each setting is one key; writing one key keeps the others.
internal static class Settings
{
    // Tests point this at a temporary file so they never touch the user's real settings.
    public static string? PathOverride { get; set; }

    private static readonly object Sync = new();

    private static string FilePath => PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZestDrop", "settings.json");

    public static string? GetString(string key)
    {
        lock (Sync)
            return Read()[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    public static bool GetBool(string key, bool fallback)
    {
        lock (Sync)
            return Read()[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
    }

    public static void Set(string key, string value) => Write(key, JsonValue.Create(value));

    public static void Set(string key, bool value) => Write(key, JsonValue.Create(value));

    private static JsonObject Read()
    {
        try
        { return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new JsonObject(); }
    }

    private static void Write(string key, JsonNode? value)
    {
        lock (Sync)
        {
            try
            {
                var settings = Read();
                settings[key] = value;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // Write a temporary file first so a crash can never leave a half-written settings file.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, settings.ToJsonString());
                File.Move(temp, FilePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
