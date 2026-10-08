using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ZestDrop;

// "Process with ZestDrop" in Explorer's right-click menu, and the hand-over of files from a second launch to the running copy.
internal static class ExplorerIntegration
{
    private const string Verb = "ZestDrop";
    private static readonly string[] Roots = [@"Software\Classes\*\shell\" + Verb, @"Software\Classes\Directory\shell\" + Verb];

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    // The Store package cannot write to the real registry, so the entry is offered to the portable build only.
    public static bool Available { get { int length = 0; return GetCurrentPackageFullName(ref length, null) == 15700; } }

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Roots[0]);
            return key != null;
        }
    }

    public static void Set(bool on)
    {
        foreach (string root in Roots)
        {
            if (!on)
            { Registry.CurrentUser.DeleteSubKeyTree(root, false); continue; }
            string exe = Environment.ProcessPath ?? "";
            using var key = Registry.CurrentUser.CreateSubKey(root);
            key.SetValue("MUIVerb", L.T("用 ZestDrop 处理…"));
            key.SetValue("Icon", exe);
            key.SetValue("MultiSelectModel", "Player");
            using var command = key.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" --open \"%1\"");
        }
    }

    // --- hand-over between launches -------------------------------------------------------------------------------
    private static string Inbox => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZestDrop", "open");

    public const string EventName = @"Local\ZestDrop_20261003_Open";

    public static string[] PathsIn(string[] args) => args.Length > 1 && args[0] == "--open" ? args.Skip(1).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray() : [];

    public static void Send(string[] paths)
    {
        Directory.CreateDirectory(Inbox);
        File.WriteAllLines(Path.Combine(Inbox, Guid.NewGuid().ToString("N") + ".txt"), paths);
        using var request = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        request.Set();
    }

    public static string[][] Receive()
    {
        if (!Directory.Exists(Inbox))
            return [];
        var batches = new System.Collections.Generic.List<string[]>();
        foreach (string file in Directory.GetFiles(Inbox, "*.txt"))
        {
            try
            { batches.Add(File.ReadAllLines(file).Where(p => p.Length > 0).ToArray()); File.Delete(file); }
            catch (IOException) { }
        }
        return batches.ToArray();
    }
}
