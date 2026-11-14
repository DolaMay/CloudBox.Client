using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using CloudBox.Client.Services;

namespace CloudBox.Client.Platforms.Android;

[Service(Exported = false,
         ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public class BackupForegroundService : Service
{
    public const string ActionCancel = "cloudbox.cancel_backup";

    private const int NotificationId = 9001;
    private const string ChannelId = "cloudbox_backup";

    private static volatile bool _isRunning;
    private static CancellationTokenSource? _cts;

    public static bool IsRunning => _isRunning;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent, StartCommandFlags flags, int startId)
    {
        Log($"OnStartCommand action={intent?.Action}, _isRunning={_isRunning}");

        if (intent?.Action == ActionCancel)
        {
            Log("cancel requested via Intent");
            try { _cts?.Cancel(); } catch { }
            return StartCommandResult.NotSticky;
        }

        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification("准备中..."));

        if (_isRunning)
        {
            Log("already running, ignore start");
            return StartCommandResult.NotSticky;
        }

        var baseUrl = AppSettingsStore.ServerUrl;
        var apiKey = AppSettingsStore.ApiKey;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            UpdateNotification("未配置服务器，已停止");
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var deviceFolder = AndroidDeviceInfoProvider.GetDeviceFolderName();
        var coordinator = new BackupCoordinator(baseUrl, apiKey, deviceFolder);

        _ = Task.Run(async () =>
        {
            var summary = "未知";
            Log("background task started");
            try
            {
                var progress = new Progress<string>(msg => UpdateNotification(msg));
                var result = await coordinator.RunAsync(progress, token);
                summary = $"{result.Uploaded} 上传, {result.Skipped} 跳过, {result.Failed} 失败";
                Log($"backup completed: {summary}");
                UpdateNotification($"完成: {summary}");
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                summary = "已取消";
                Log("backup cancelled by user");
                UpdateNotification(summary);
            }
            catch (System.OperationCanceledException)
            {
                // 内部 OCE 泄漏 —— 极罕见，不视为用户取消
                summary = "内部错误（已停止）";
                Log("WARNING: leaked OperationCanceledException without user cancel");
                UpdateNotification(summary);
            }
            catch (Exception ex)
            {
                summary = $"失败: {ex.Message}";
                Log($"backup failed: {ex}");
                UpdateNotification(summary);
            }
            finally
            {
                try
                {
                    Preferences.Default.Set("last_backup_result", summary);
                    Preferences.Default.Set("last_backup_time",
                        DateTimeOffset.UtcNow.ToString("o"));
                }
                catch { }
                try { StopForeground(StopForegroundFlags.Remove); } catch { }
                try { StopSelf(); } catch { }
                _isRunning = false;
                Log($"background task ended: summary={summary}");
            }
        }, token);

        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        // 关键诊断：如果这行日志出现在「自动取消」场景，说明系统杀掉了服务
        Log($"OnDestroy called, _isRunning={_isRunning}");
        try { _cts?.Cancel(); } catch { }
        try { StopForeground(StopForegroundFlags.Remove); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        if (nm == null) return;

        var channel = new NotificationChannel(ChannelId, "CloudBox 备份",
            NotificationImportance.Low) { Description = "备份进度通知" };
        nm.CreateNotificationChannel(channel);
    }

    private void UpdateNotification(string text)
    {
        try
        {
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            nm?.Notify(NotificationId, BuildNotification(text));
        }
        catch { }
    }

    private Notification BuildNotification(string text)
    {
        var openIntent = new Intent(this, typeof(MainActivity));
        var openPending = PendingIntent.GetActivity(this, 0, openIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var cancelIntent = new Intent(this, typeof(BackupForegroundService));
        cancelIntent.SetAction(ActionCancel);
        var cancelPending = PendingIntent.GetService(this, 1, cancelIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("CloudBox 备份")
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .SetContentIntent(openPending)
            .AddAction(0, "取消", cancelPending)
            .Build();
    }

    private static void Log(string msg)
    {
#if ANDROID
        global::Android.Util.Log.Debug("CloudBox", msg);
#else
        System.Diagnostics.Debug.WriteLine($"[CloudBox] {msg}");
#endif
    }
}