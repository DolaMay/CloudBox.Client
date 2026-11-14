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

    public static void Stop()
    {
        var ctx = Platform.AppContext;
        ctx.StopService(new Intent(ctx, typeof(BackupForegroundService)));
    }

    public static bool IsRunning => BackupForegroundService.IsRunning;
}