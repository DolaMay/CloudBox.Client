using AndroidX.Work;

namespace CloudBox.Client.Platforms.Android;

public static class WorkScheduler
{
    private const string UniqueName = "CloudBoxPeriodicBackup";

    public static void Schedule(TimeSpan interval)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Unmetered)  // 仅 Wi-Fi
            .SetRequiresBatteryNotLow(true)                 // 电量不低
            .Build();

        var request = PeriodicWorkRequest
            .Builder.From<BackupWorker>(interval)
            .SetConstraints(constraints)
            .Build();

        WorkManager.GetInstance(
            Microsoft.Maui.ApplicationModel.Platform.AppContext)
            .EnqueueUniquePeriodicWork(
                UniqueName,
                ExistingPeriodicWorkPolicy.Update,
                request);
    }

    public static void Cancel()
    {
        WorkManager.GetInstance(
            Microsoft.Maui.ApplicationModel.Platform.AppContext)
            .CancelUniqueWork(UniqueName);
    }
}