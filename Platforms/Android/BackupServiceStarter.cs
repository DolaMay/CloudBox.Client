using Android.Content;
using Microsoft.Maui.ApplicationModel;

namespace CloudBox.Client.Platforms.Android;

public static class BackupServiceStarter
{
    public static void Start()
    {
        var ctx = Platform.AppContext;
        var intent = new Intent(ctx, typeof(BackupForegroundService));

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            ctx.StartForegroundService(intent);
        else
            ctx.StartService(intent);
    }

    /// <summary>
    /// 请求取消：通过 Intent action 通知 Service，让 Service 自己 cancel token 并走 finally。
    /// 不用 StopService，避免 Service 实例在任务完成前被销毁导致通知残留。
    /// </summary>
    public static void Cancel()
    {
        var ctx = Platform.AppContext;
        var intent = new Intent(ctx, typeof(BackupForegroundService));
        intent.SetAction(BackupForegroundService.ActionCancel);
        ctx.StartService(intent);
    }

    public static void Stop()
    {
        var ctx = Platform.AppContext;
        ctx.StopService(new Intent(ctx, typeof(BackupForegroundService)));
    }

    public static bool IsRunning => BackupForegroundService.IsRunning;
}