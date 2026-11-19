using BackgroundTasks;
using CloudBox.Client.Services;
using Foundation;

namespace CloudBox.Client.Platforms.iOS;

public static class IOSBackgroundTaskManager
{
    public const string BackupTaskId = "com.companyname.cloudbox.backup";

    public static void Register()
    {
        BGTaskScheduler.Shared.Register(BackupTaskId, null, task =>
        {
            var processingTask = (BGProcessingTask)task;
            processingTask.ExpirationHandler = () => processingTask.SetTaskCompleted(false);

            Task.Run(async () =>
            {
                try
                {
                    if (!IOSBackupService.IsRunning &&
                        !string.IsNullOrEmpty(AppSettingsStore.ServerUrl))
                    {
                        IOSBackupService.Start();
                    }
                    processingTask.SetTaskCompleted(true);
                }
                catch
                {
                    processingTask.SetTaskCompleted(false);
                }
            });
        });
    }

    public static void ScheduleBackup()
    {
        var request = new BGProcessingTaskRequest(BackupTaskId)
        {
            RequiresNetworkConnectivity = true,
            RequiresExternalPower = true
        };
        BGTaskScheduler.Shared.Submit(request, out _);
    }
}