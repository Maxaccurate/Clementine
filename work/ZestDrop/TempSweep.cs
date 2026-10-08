using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ZestDrop;

// The worker stages outputs as ".zestdrop-*" entries beside the inputs and removes them in Python finally blocks.
// Killing the worker skips those blocks, so remember what existed before the job and remove only what it added.
// With a job token (the worker's ZESTDROP_JOB), only that job's ".zestdrop-<token>-*" entries are touched, so
// cancelling one job never removes the staging files of another job running in the same folder.
internal sealed class TempSweep
{
    private readonly string prefix;
    private readonly Dictionary<string, HashSet<string>> baseline = new(StringComparer.OrdinalIgnoreCase);
    public TempSweep(IEnumerable<string> paths, string? token = null, string? outputFolder = null)
    {
        prefix = ".zestdrop-" + (string.IsNullOrEmpty(token) ? "" : token + "-");
        foreach (string path in paths)
        {
            string? folder;
            try
            { folder = Path.GetDirectoryName(Path.GetFullPath(path)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (folder != null && !baseline.ContainsKey(folder))
                baseline[folder] = Entries(folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        // Staging files also live in the chosen output folder.
        if (outputFolder != null && !baseline.ContainsKey(outputFolder))
            baseline[outputFolder] = Entries(outputFolder).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    private string[] Entries(string folder)
    {
        try
        { return Directory.GetFileSystemEntries(folder, prefix + "*"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    public async Task Run()
    {
        // Killed child processes release their handles shortly after termination, so retry briefly.
        for (int attempt = 0; attempt < 15; attempt++)
        {
            bool pending = false;
            foreach (var (folder, known) in baseline)
                foreach (string entry in Entries(folder))
                {
                    if (known.Contains(entry))
                        continue;
                    try
                    { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { pending = true; }
                }
            if (!pending)
                return;
            await Task.Delay(200).ConfigureAwait(false);
        }
    }
}
