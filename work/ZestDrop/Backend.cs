using System;
using System.Collections.Generic;
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
        // The Store installs the app into a read-only folder, so Python must not try to write bytecode caches there.
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        foreach (string arg in arguments)
            info.ArgumentList.Add(arg);
        return info;
    }
    public static async Task Run(string[] arguments, CancellationToken cancellation = default, Action<Process>? onStart = null, IProgress<JobProgress>? progress = null)
    {
        using var watch = arguments.Length >= 3 && arguments[0] == "--job" ? WatchProgress(arguments[2], progress) : null;
        using var process = Process.Start(StartInfo(arguments)) ?? throw new IOException(L.T("无法启动本地处理引擎"));
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
            if (arguments.Length > 2 && File.Exists(arguments[2]))
            {
                using var json = JsonDocument.Parse(File.ReadAllText(arguments[2]));
                if (json.RootElement.TryGetProperty("Error", out var message))
                    detail = message.GetString() ?? detail;
            }
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
