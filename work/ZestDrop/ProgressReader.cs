using System;
using System.IO;
using System.Text.Json;

namespace ZestDrop;

internal static class ProgressReader
{
    public static JobProgress? Read(string path)
    {
        try
        {
            var value = JsonSerializer.Deserialize<JobProgress>(File.ReadAllText(path));
            return value is { Total: > 0, Processed: >= 0 } && value.Processed <= value.Total ? value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }
}
