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
    private const int NotificationId = 9001;
    private const string ChannelId = "cloudbox_backup";
    private static CancellationTokenSource? _cts;
    private static bool _isRunning;

    public static bool IsRunning => _isRunning;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();
        StartForeground(NotificationId, BuildNotification("准备中..."));

        if (_isRunning)
            return StartCommandResult.NotSticky;

        var baseUrl = AppSettingsStore.ServerUrl;
        var apiKey = AppSettingsStore.ApiKey;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            UpdateNotification("未配置服务器，已停止");
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
            try
            {
                var progress = new Progress<string>(msg => UpdateNotification(msg));
                var result = await coordinator.RunAsync(progress, token);
                summary = $"{result.Uploaded} 上传, {result.Skipped} 跳过, {result.Failed} 失败";
                UpdateNotification($"完成: {summary}");
            }
            catch (System.OperationCanceledException)
            {
                summary = "已取消";
                UpdateNotification(summary);
            }
            catch (Exception ex)
            {
                summary = $"失败: {ex.Message}";
                UpdateNotification(summary);
            }
            finally
            {
                _isRunning = false;
                try
                {
                    Preferences.Default.Set("last_backup_result", summary);
                    Preferences.Default.Set("last_backup_time",
                        DateTimeOffset.UtcNow.ToString("o"));
                }
                catch { }
                try { StopForeground(StopForegroundFlags.Remove); } catch { }
                StopSelf();
            }
        }, token);

        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        _isRunning = false;
        base.OnDestroy();
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        if (nm == null) return;

        var channel = new NotificationChannel(ChannelId, "CloudBox 备份",
            NotificationImportance.Low)
        {
            Description = "备份进度通知"
        };
        nm.CreateNotificationChannel(channel);
    }

    private void UpdateNotification(string text)
    {
        var nm = (NotificationManager?)GetSystemService(NotificationService);
        nm?.Notify(NotificationId, BuildNotification(text));
    }

    private Notification BuildNotification(string text)
    {
        var intent = new Intent(this, typeof(MainActivity));
        var pending = PendingIntent.GetActivity(this, 0, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        return new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("CloudBox 备份")
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .SetContentIntent(pending)
            .Build();
    }
}