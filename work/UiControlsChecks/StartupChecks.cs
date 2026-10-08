using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using ZestDrop;

internal static class StartupChecks
{
    public static void Run(Action<string, Action> check)
    {
        string testKey = @"Software\ZestDrop\Tests\Startup-" + Guid.NewGuid().ToString("N");
        var provider = new RegistryStartupProvider(testKey, "ZestDrop", @"C:\Apps with spaces\工具 & files\ZestDrop.exe");
        void Require(bool condition) { if (!condition) throw new Exception("Unexpected startup registration state."); }
        try
        {
            check("startup is disabled by default and reading does not create a registry key", () =>
            {
                Require(!provider.GetAsync().GetAwaiter().GetResult().Enabled);
                using var key = Registry.CurrentUser.OpenSubKey(testKey); Require(key == null);
                provider.RefreshPath();
                using var stillMissing = Registry.CurrentUser.OpenSubKey(testKey); Require(stillMissing == null);
            });
            check("startup enable and disable quote the executable and preserve other entries", () =>
            {
                using (var key = Registry.CurrentUser.CreateSubKey(testKey)) key.SetValue("unrelated", "keep");
                Require(provider.SetAsync(true).GetAwaiter().GetResult().Enabled);
                using (var key = Registry.CurrentUser.OpenSubKey(testKey))
                    Require((string?)key?.GetValue("ZestDrop") == "\"C:\\Apps with spaces\\工具 & files\\ZestDrop.exe\" --startup");
                Require(!provider.SetAsync(false).GetAwaiter().GetResult().Enabled);
                using (var key = Registry.CurrentUser.OpenSubKey(testKey))
                    Require(key?.GetValue("ZestDrop") == null && (string?)key?.GetValue("unrelated") == "keep");
            });
            check("moving an enabled portable app refreshes its path without enabling a disabled app", () =>
            {
                provider.SetAsync(true).GetAwaiter().GetResult();
                var moved = new RegistryStartupProvider(testKey, "ZestDrop", @"D:\New folder\ZestDrop.exe");
                moved.RefreshPath();
                using (var key = Registry.CurrentUser.OpenSubKey(testKey)) Require((string?)key?.GetValue("ZestDrop") == "\"D:\\New folder\\ZestDrop.exe\" --startup");
                moved.SetAsync(false).GetAwaiter().GetResult(); moved.RefreshPath();
                Require(!moved.GetAsync().GetAwaiter().GetResult().Enabled);
            });
            check("sign-in launches are distinct from manual or debug launches", () =>
            {
                Require(StartupManager.IsStartupLaunch(["--startup"]));
                Require(StartupManager.IsStartupLaunch(["--STARTUP"]));
                Require(!StartupManager.IsStartupLaunch([]) && !StartupManager.IsStartupLaunch(["--debug-tool", "startup"]));
            });
            check("startup failures are reported without pretending the option changed", () =>
            {
                StartupManager.ProviderOverride = new FailingProvider();
                var result = StartupManager.SetAsync(true).GetAwaiter().GetResult();
                Require(!result.Enabled && result.Message != null);
                StartupManager.ProviderOverride = null;
            });
        }
        finally
        {
            StartupManager.ProviderOverride = null;
            Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
        }
    }
    private sealed class FailingProvider : IStartupProvider
    {
        public Task<StartupStatus> GetAsync() => Task.FromResult(new StartupStatus(false));
        public Task<StartupStatus> SetAsync(bool enabled) => throw new UnauthorizedAccessException("Test access denial");
    }
}
