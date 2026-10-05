using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ZestDrop;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
    public static bool ShellFileWindow()
    {
        var name = new StringBuilder(256);
        GetClassName(GetForegroundWindow(), name, name.Capacity);
        return name.ToString() is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    public static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
}
