using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ZestDrop;

internal record StartupStatus(bool Enabled, bool CanChange = true, string? Message = null);
internal interface IStartupProvider
{
    Task<StartupStatus> GetAsync();
    Task<StartupStatus> SetAsync(bool enabled);
}

internal static class StartupManager
{
    internal const string TaskId = "ZestDropStartup";
    private static IStartupProvider? provider;
    internal static IStartupProvider? ProviderOverride { get; set; }
    public static event Action? Changed;
    private static IStartupProvider Provider => ProviderOverride ?? (provider ??= CreateProvider());
    private static IStartupProvider CreateProvider()
    {
#if PACKAGED_STARTUP
        if (IsPackaged) return new PackagedStartupProvider();
#endif
        return new RegistryStartupProvider(@"Software\Microsoft\Windows\CurrentVersion\Run", "ZestDrop", Path.Combine(AppContext.BaseDirectory, "ZestDrop.exe"));
    }
    internal static bool IsPackaged { get { uint length = 0; return GetCurrentPackageFullName(ref length, IntPtr.Zero) == 122; } }
    public static bool IsStartupLaunch(string[] args)
    {
        if (args.Contains("--startup", StringComparer.OrdinalIgnoreCase)) return true;
#if PACKAGED_STARTUP
        if (IsPackaged) return PackagedStartupProvider.IsStartupActivation();
#endif
        return false;
    }
    public static async Task<StartupStatus> GetAsync()
    {
        try { return await Provider.GetAsync(); }
        catch (Exception ex) { return new(false, false, L.T("无法读取开机自启设置：") + ex.Message); }
    }
    public static async Task<StartupStatus> SetAsync(bool enabled)
    {
        try
        {
            var status = await Provider.SetAsync(enabled);
            Changed?.Invoke();
            return status;
        }
        catch (Exception ex) { return new((await GetAsync()).Enabled, true, L.T("无法更改开机自启设置：") + ex.Message); }
    }
    // Moving the portable app must not leave an enabled entry pointing at an obsolete folder.
    public static void RefreshPortablePath()
    {
        if (Provider is RegistryStartupProvider registry) registry.RefreshPath();
    }
    public static void OpenWindowsSettings() => Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}

internal sealed class RegistryStartupProvider(string keyPath, string valueName, string executable) : IStartupProvider
{
    internal string Command => QuoteCommand(executable);
    internal static string QuoteCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"')) throw new ArgumentException("A full executable path is required.");
        return $"\"{executable}\" --startup";
    }
    private bool IsRegistered()
    { using var key = Registry.CurrentUser.OpenSubKey(keyPath); return key?.GetValue(valueName) is string command && !string.IsNullOrWhiteSpace(command); }
    private bool WindowsDisabled()
    {
        // Read the OS override, never change it to bypass a choice made in Windows Settings/Task Manager.
        if (keyPath != @"Software\Microsoft\Windows\CurrentVersion\Run") return false;
        using var approval = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        return approval?.GetValue(valueName) is byte[] state && state.Length >= 4 && state[0] is 3 or 7;
    }
    public Task<StartupStatus> GetAsync()
    {
        bool registered = IsRegistered(), blocked = registered && WindowsDisabled();
        return Task.FromResult(new StartupStatus(registered && !blocked, !blocked,
            blocked ? L.T("Windows 已禁用自启，请在系统启动应用设置中更改。") : null));
    }
    public Task<StartupStatus> SetAsync(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, true) ?? throw new IOException("Cannot open startup settings.");
        if (enabled) key.SetValue(valueName, Command, RegistryValueKind.String);
        else key.DeleteValue(valueName, false);
        return GetAsync();
    }
    internal void RefreshPath()
    {
        try
        {
            if (!IsRegistered()) return;
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key?.GetValue(valueName) is string command && command != Command) key.SetValue(valueName, Command, RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
    }
}
