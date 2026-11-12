using Android.Content;
using AndroidX.Work;
using CloudBox.Client.Services;

namespace CloudBox.Client.Platforms.Android;

public class BackupWorker : Worker
{
    public BackupWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams) { }

    public override Result DoWork()
    {
        try
        {
            var baseUrl = AppSettingsStore.ServerUrl;
            var apiKey  = AppSettingsStore.ApiKey;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
                return Result.InvokeFailure();

            var coordinator = new BackupCoordinator(
                baseUrl, apiKey, DeviceInfo.Name ?? "android");

            // Worker 在后台线程，同步等待即可
            var result = coordinator.RunAsync(null, CancellationToken.None)
                                    .GetAwaiter().GetResult();

            return result.Failed > 0 ? Result.InvokeRetry() : Result.InvokeSuccess();
        }
        catch
        {
            return Result.InvokeRetry();
        }
    }
}