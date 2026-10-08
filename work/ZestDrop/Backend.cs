using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZestDrop;

internal static class Backend
{
    public static ProcessStartInfo StartInfo(params string[] arguments)
    {
        string baseDir = AppContext.BaseDirectory;
        var info = new ProcessStartInfo(Path.Combine(baseDir, "runtime", "python", "python.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = baseDir };
        info.ArgumentList.Add(Path.Combine(baseDir, "backend", "worker.py"));
        // Messages from the engine (errors, page headings in text exports) follow the app's language.
        info.Environment["ZESTDROP_LANG"] = L.Language;
        // The Store installs the app into a read-only folder, so Python must not write bytecode caches next to its libraries.
        // Without any cache every job recompiles them (1–2 s slower), so keep the cache in the user's profile instead.
        info.Environment["PYTHONPYCACHEPREFIX"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZestDrop", "pycache");
        // A copy of the user's environment is inherited; if it asks Python not to write bytecode, the cache above would stay empty.
        info.Environment.Remove("PYTHONDONTWRITEBYTECODE");
        // Results go where the user chose (tray menu); an empty value means next to the source file.
        info.Environment["ZESTDROP_OUTPUT_DIR"] = OutputFolder.Current ?? "";
        foreach (string arg in arguments)
            info.ArgumentList.Add(arg);
        return info;
    }
    // jobToken tags the worker's staging files, so cleaning up after a cancel touches only this job's files.
    public static async Task Run(string[] arguments, CancellationToken cancellation = default, Action<Process>? onStart = null, IProgress<JobProgress>? progress = null, string? jobToken = null)
    {
        using var watch = arguments.Length >= 3 && arguments[0] == "--job" ? WatchProgress(arguments[2], progress) : null;
        var start = StartInfo(arguments);
        if (jobToken != null)
            start.Environment["ZESTDROP_JOB"] = jobToken;
        using var process = Process.Start(start) ?? throw new IOException(L.T("无法启动本地处理引擎"));
        onStart?.Invoke(process);
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        try
        { await process.WaitForExitAsync(cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        await output.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string detail = await error.ConfigureAwait(false);
            // The engine writes its own message into the result file when it stops on an error.
            try
            {
                if (arguments.Length > 2 && File.Exists(arguments[2]))
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(arguments[2]));
                    if (json.RootElement.TryGetProperty("Error", out var message))
                        detail = message.GetString() ?? detail;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
            throw new IOException(string.IsNullOrWhiteSpace(detail) ? L.T("本地引擎未完成处理") : detail);
        }
    }
    public static async Task<JsonElement> Inspect(string path, string folder, CancellationToken token, int frame = -1)
    {
        folder = Path.Combine(folder, "inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string response = Path.Combine(folder, "inspect.json");
        await Run(["--inspect", path, response, folder, frame.ToString()], token);
        return JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(response));
    }
    public static async Task<JsonElement> Preview(ConversionJob job, string folder, CancellationToken token, string mode = "--preview")
    {
        folder = Path.Combine(folder, "request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string request = Path.Combine(folder, "preview-request.json"), response = Path.Combine(folder, "preview-result.json");
        File.WriteAllText(request, JsonSerializer.Serialize(job));
        await Run([mode, request, response, folder], token);
        return JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(response));
    }
    public static IDisposable? WatchProgress(string response, IProgress<JobProgress>? progress)
    {
        if (progress == null)
            return null;
        return new Timer(_ => { var value = ProgressReader.Read(response + ".progress"); if (value != null) progress.Report(value); }, null, 0, 200);
    }
    public static async Task<BatchResult> Execute(ConversionJob job, CancellationToken token, IProgress<JobProgress>? progress = null)
    {
        string folder = Path.Combine(Journal.DirectoryPath, "qa-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string request = Path.Combine(folder, "request.json"), response = Path.Combine(folder, "result.json");
        File.WriteAllText(request, JsonSerializer.Serialize(job));
        await Run(["--job", request, response], token, progress: progress);
        return JsonSerializer.Deserialize<BatchResult>(File.ReadAllText(response))!;
    }
}
