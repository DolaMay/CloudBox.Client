using CloudBox.Client.Services;
using CloudBox.Core.Services;

namespace CloudBox.Client.Platforms.iOS;

public static class IOSBackupService
{
    private static volatile bool _isRunning;
    private static CancellationTokenSource? _cts;

    public static bool IsRunning => _isRunning;

    public static void Start()
    {
        if (_isRunning) return;

        var baseUrl = AppSettingsStore.ServerUrl;
        var apiKey = AppSettingsStore.ApiKey;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey)) return;

        _isRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var deviceFolder = IOSDeviceInfoProvider.GetDeviceFolderName();

        var coordinator = new BackupCoordinator(
            baseUrl, apiKey, deviceFolder,
            new IOSMediaScanner(),
            new IOSContentUploader());

        _ = Task.Run(async () =>
        {
            var summary = "未知";
            try
            {
                var progress = new Progress<string>(msg => { });
                var result = await coordinator.RunAsync(progress, token);
                summary = $"{result.Uploaded} 上传, {result.Skipped} 跳过, {result.Failed} 失败";
            }
            catch (System.OperationCanceledException) when (token.IsCancellationRequested)
            {
                summary = "已取消";
            }
            catch (System.OperationCanceledException)
            {
                summary = "内部错误（已停止）";
            }
            catch (Exception ex)
            {
                summary = $"失败: {ex.Message}";
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
                _isRunning = false;
                try { _cts?.Dispose(); } catch { }
                _cts = null;
            }
        }, token);
    }

    public static void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }
}