using CloudBox.Client.Services;
#if ANDROID
using CloudBox.Client.Platforms.Android;
#endif

namespace CloudBox.Client;

public partial class MainPage : ContentPage
{
    private IDispatcherTimer? _stateTimer;

    public MainPage()
    {
        InitializeComponent();
        ServerUrlEntry.Text = AppSettingsStore.ServerUrl;
        ApiKeyEntry.Text = AppSettingsStore.ApiKey;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        RefreshUiState();
        _stateTimer?.Stop();
        _stateTimer = Dispatcher.CreateTimer();
        _stateTimer.Interval = TimeSpan.FromSeconds(1);
        _stateTimer.Tick += (s, e) => RefreshUiState();
        _stateTimer.Start();
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _stateTimer?.Stop();
        _stateTimer = null;
    }

    private void RefreshUiState()
    {
#if ANDROID
        var running = BackupServiceStarter.IsRunning;
        BackupButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        DiscoverButton.IsEnabled = !running;

        if (running)
        {
            StatusLabel.Text = "备份进行中…（下拉通知栏查看进度，可息屏）";
            return;
        }

        // 服务停止后显示上次结果
        var lastResult = Preferences.Default.Get("last_backup_result", "");
        var lastTimeStr = Preferences.Default.Get("last_backup_time", "");
        if (!string.IsNullOrEmpty(lastResult) && !string.IsNullOrEmpty(lastTimeStr) &&
            DateTimeOffset.TryParse(lastTimeStr, out var lastTime))
        {
            var local = lastTime.ToLocalTime().ToString("MM-dd HH:mm");
            if (StatusLabel.Text?.StartsWith("备份进行中") != false)
                StatusLabel.Text = $"上次备份 {local}: {lastResult}";
        }
#endif
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
        AppSettingsStore.ApiKey = apiKey;

#if ANDROID
        if (BackupServiceStarter.IsRunning)
        {
            StatusLabel.Text = "备份已经在运行中";
            return;
        }

        // 1. 媒体权限
        var (imagesOk, videosOk) = await AndroidPermissionHelper.RequestMediaPermissionsAsync();
        if (!imagesOk)
        {
            StatusLabel.Text = "未获得照片读取权限，无法备份";
            return;
        }
        if (!videosOk)
        {
            var cont = await DisplayAlert("提示",
                "视频权限被拒绝，本次只备份照片。需要备份视频请去系统设置手动开启。",
                "继续", "取消");
            if (!cont) return;
        }

        // 2. 通知权限（Android 13+）
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var notif = await Permissions.RequestAsync<Permissions.PostNotifications>();
            if (notif != PermissionStatus.Granted)
            {
                var cont = await DisplayAlert("提示",
                    "通知权限被拒绝，前台服务会继续运行，但通知栏看不到进度。",
                    "继续", "取消");
                if (!cont) return;
            }
        }

        // 3. 电池优化（提示但不阻断）
        if (!BatteryOptimizationHelper.IsIgnoringBatteryOptimizations())
        {
            var goSettings = await DisplayAlert("建议关闭电池优化",
                "长时间备份时，系统电池优化可能暂停前台服务。\n" +
                "建议把 CloudBox 的电池优化设为「不限制」。\n\n" +
                "可以先去设置，返回后再点「开始扫描并备份」。",
                "去设置", "直接开始");
            if (goSettings)
            {
                BatteryOptimizationHelper.OpenBatteryOptimizationSettings();
                StatusLabel.Text = "请返回本页后再次点击「开始扫描并备份」";
                return;
            }
        }

        // 4. 启动前台服务
        Preferences.Default.Remove("last_backup_result");
        Preferences.Default.Remove("last_backup_time");
        StatusLabel.Text = "备份已启动，可息屏或切换应用";
        BackupServiceStarter.Start();
        RefreshUiState();
#else
        // 非 Android 平台：直接同步调用（Windows 调试用）
        BackupButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusLabel.Text = "备份进行中...";
        try
        {
            var deviceName = DeviceInfo.Name ?? "device";
            var coordinator = new BackupCoordinator(baseUrl, apiKey, deviceName);
            var progress = new Progress<string>(msg =>
                MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = msg));
            var result = await coordinator.RunAsync(progress, CancellationToken.None);
            StatusLabel.Text =
                $"完成：上传 {result.Uploaded}，跳过 {result.Skipped}，失败 {result.Failed}";
        }
        finally
        {
            BackupButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
#endif
    }

    private void OnCancelClicked(object sender, EventArgs e)
    {
#if ANDROID
        BackupServiceStarter.Stop();
        StatusLabel.Text = "已请求取消，正在停止…";
        CancelButton.IsEnabled = false;
#endif
    }

    private async void OnDiscoverClicked(object sender, EventArgs e)
    {
#if ANDROID
        DiscoverButton.IsEnabled = false;
        StatusLabel.Text = "正在扫描局域网内的 CloudBox 服务...";
        try
        {
            var servers = await MdnsDiscovery.DiscoverAsync(
                TimeSpan.FromSeconds(5), CancellationToken.None);

            if (servers.Count == 0)
            {
                StatusLabel.Text = "未发现服务器。请确认手机和 PC 在同一 Wi-Fi，且 PC 端已启动。";
                return;
            }

            var first = servers[0];
            ServerUrlEntry.Text = $"http://{first.Host}:{first.Port}";
            StatusLabel.Text = $"已发现: {first.Name} ({first.Host}:{first.Port})。";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"发现失败: {ex.Message}";
        }
        finally
        {
            DiscoverButton.IsEnabled = !BackupServiceStarter.IsRunning;
        }
#endif
    }
}