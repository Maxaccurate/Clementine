#if PACKAGED_STARTUP
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace ZestDrop;

internal sealed class PackagedStartupProvider : IStartupProvider
{
    private static StartupStatus Status(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => new(true),
        StartupTaskState.EnabledByPolicy => new(true, false, L.T("开机自启由 Windows 系统策略管理。")),
        StartupTaskState.DisabledByPolicy => new(false, false, L.T("开机自启由 Windows 系统策略管理。")),
        StartupTaskState.DisabledByUser => new(false, false, L.T("Windows 已禁用自启，请在系统启动应用设置中更改。")),
        _ => new(false)
    };
    public async Task<StartupStatus> GetAsync() => Status((await StartupTask.GetAsync(StartupManager.TaskId)).State);
    public async Task<StartupStatus> SetAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(StartupManager.TaskId);
        if (enabled) return Status(await task.RequestEnableAsync());
        task.Disable(); return Status(task.State);
    }
    internal static bool IsStartupActivation()
    {
        try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
        catch { return false; }
    }
}
#endif
