using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZestDrop;

// Suspends a worker and every process it started (FFmpeg, 7-Zip, the Office bridge), and resumes them later.
// Disposing resumes anything still suspended, so a paused job can never be left frozen.
internal sealed class ProcessTreePause : IDisposable
{
    private const uint SuspendResume = 0x0800, QueryLimited = 0x1000, SnapshotProcesses = 0x2;
    private readonly List<(IntPtr Handle, int Id)> suspended = [];
    private bool resumed;

    private ProcessTreePause() { }

    public int Count => suspended.Count;

    public static ProcessTreePause Suspend(Process root)
    {
        var pause = new ProcessTreePause();
        var created = new Dictionary<int, long>();
        try
        {
            // Freeze the root first so it cannot start new children while the tree is collected.
            if (!pause.TrySuspend(root.Id, created))
                throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("无法暂停处理进程"));
            // A child may have been mid-launch; a few passes catch processes that appear late.
            for (int pass = 0; pass < 3; pass++)
            {
                bool found = false;
                foreach (var (id, parent) in Snapshot())
                {
                    if (created.ContainsKey(id) || !created.TryGetValue(parent, out long parentCreated))
                        continue;
                    // A parent id can be reused by an unrelated process; real children never predate their parent.
                    if (CreationTime(id) is long childCreated && childCreated >= parentCreated && pause.TrySuspend(id, created))
                        found = true;
                }
                if (!found)
                    break;
            }
            return pause;
        }
        catch
        {
            pause.Dispose();
            throw;
        }
    }

    public void Resume()
    {
        if (resumed)
            return;
        resumed = true;
        // Children first, so the root never resumes into a still-frozen pipeline.
        for (int i = suspended.Count - 1; i >= 0; i--)
            NtResumeProcess(suspended[i].Handle);
    }

    public void Dispose()
    {
        Resume();
        foreach (var (handle, _) in suspended)
            CloseHandle(handle);
        suspended.Clear();
    }

    private bool TrySuspend(int id, Dictionary<int, long> created)
    {
        IntPtr handle = OpenProcess(SuspendResume | QueryLimited, false, id);
        if (handle == IntPtr.Zero)
            return false;
        if (!GetProcessTimes(handle, out long creation, out _, out _, out _) || NtSuspendProcess(handle) != 0)
        {
            CloseHandle(handle);
            return false;
        }
        suspended.Add((handle, id));
        created[id] = creation;
        return true;
    }

    private static long? CreationTime(int id)
    {
        IntPtr handle = OpenProcess(QueryLimited, false, id);
        if (handle == IntPtr.Zero)
            return null;
        try
        { return GetProcessTimes(handle, out long creation, out _, out _, out _) ? creation : null; }
        finally { CloseHandle(handle); }
    }

    private static List<(int Id, int Parent)> Snapshot()
    {
        var result = new List<(int, int)>();
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == new IntPtr(-1))
            return result;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                result.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr handle);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
}
