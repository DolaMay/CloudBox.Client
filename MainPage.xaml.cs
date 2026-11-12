#if ANDROID
using CloudBox.Client.Platforms.Android;
#endif
using System.Collections.ObjectModel;
using CloudBox.Client.Services;

namespace CloudBox.Client;

public partial class MainPage : ContentPage
{
    private CancellationTokenSource? _cts;

    public MainPage()
    {
        InitializeComponent();
        ServerUrlEntry.Text = AppSettingsStore.ServerUrl;
        ApiKeyEntry.Text = AppSettingsStore.ApiKey;
    }

    private async void OnBackupClicked(object sender, EventArgs e)
    {
        var baseUrl = ServerUrlEntry.Text?.Trim();
        var apiKey = ApiKeyEntry.Text?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            StatusLabel.Text = "请填写服务器地址和 API Key";
            return;
        }

        AppSettingsStore.ServerUrl = baseUrl;
        AppSettingsStore.ApiKey    = apiKey;

        if (!await RequestMediaPermissionAsync())
        {
            StatusLabel.Text = "未获得相册读取权限";
            return;
        }

        _cts = new CancellationTokenSource();
        BackupButton.IsEnabled = false;
        CancelButton.IsEnabled = true;

        var lines = new ObservableCollection<string>();
        var progress = new Progress<string>(msg =>
        {
            lines.Add(msg);
            StatusLabel.Text = string.Join("\n", lines.TakeLast(8));
        });

        try
        {
            var coordinator = new BackupCoordinator(baseUrl, apiKey, DeviceInfo.Name ?? "android");
            var result = await coordinator.RunAsync(progress, _cts.Token);
            StatusLabel.Text =
                $"完成：共 {result.Total}，上传 {result.Uploaded}，跳过 {result.Skipped}，失败 {result.Failed}";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "已取消";
        }
        finally
        {
            BackupButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _cts.Dispose();
            _cts = null;
        }
    }
    private async void OnBackgroundToggled(object sender, ToggledEventArgs e)
    {
    #if ANDROID
        if (!e.Value)
        {
            WorkScheduler.Cancel();
            StatusLabel.Text = "已关闭后台备份";
            return;
        }

        // 开启前先检查电池优化状态
        if (!BatteryOptimizationHelper.IsIgnoringBatteryOptimizations())
        {
            var go = await DisplayAlert(
                "需要关闭电池优化",
                "Android 的电池优化可能会在后台杀死定时备份任务。\n\n" +
                "请在打开的设置页中找到「CloudBox」，把电池优化设为「无限制」或「不优化」。\n\n" +
                "设置完成后返回本页，后台备份会自动生效。",
                "去设置", "稍后再说");

            if (go)
                BatteryOptimizationHelper.OpenBatteryOptimizationSettings();
        }

        WorkScheduler.Schedule(TimeSpan.FromMinutes(15));
        StatusLabel.Text = "已启用后台备份（每 6 小时尝试一次）。" +
                        "若长时间未触发，请回到系统设置确认本 App 的电池优化已设为「不限制」。";
    #endif
    }
    private void OnCancelClicked(object sender, EventArgs e) => _cts?.Cancel();

	private async Task<bool> RequestMediaPermissionAsync()
	{
	#if ANDROID
		return await AndroidPermissionHelper.RequestMediaPermissionsAsync();
	#else
		return true;
	#endif
	}
}