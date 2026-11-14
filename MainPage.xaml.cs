using CloudBox.Client.Services;
#if ANDROID
using CloudBox.Client.Platforms.Android;
#endif

namespace CloudBox.Client;

public partial class MainPage : ContentPage
{
#if ANDROID
    private IDispatcherTimer? _stateTimer;
    private bool _cancelRequested;
    private bool? _lastRunningState;   // ← 新增：上一次采样到的运行状态
#endif

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
#if ANDROID
        _stateTimer?.Stop();
        _stateTimer = null;
#endif
    }

    private void RefreshUiState()
    {
#if ANDROID
        var running = BackupServiceStarter.IsRunning;

        // 按钮状态每次都刷（无害，保证 UI 和 Service 状态一致）
        BackupButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        DiscoverButton.IsEnabled = !running;

        // StatusLabel 只在「运行状态变化」时更新，避免覆盖 mDNS 发现等临时消息
        if (_lastRunningState == running)
            return;

        _lastRunningState = running;

        if (running)
        {
            StatusLabel.Text = _cancelRequested
                ? "正在取消…"
                : "备份进行中…（下拉通知栏查看进度，可息屏）";
            return;
        }

        // 刚刚从 running → idle
        _cancelRequested = false;

        var lastResult = Preferences.Default.Get("last_backup_result", "");
        var lastTimeStr = Preferences.Default.Get("last_backup_time", "");
        if (!string.IsNullOrEmpty(lastResult) && !string.IsNullOrEmpty(lastTimeStr) &&
            DateTimeOffset.TryParse(lastTimeStr, out var lastTime))
        {
            var local = lastTime.ToLocalTime().ToString("MM-dd HH:mm");
            StatusLabel.Text = $"上次备份 {local}: {lastResult}";
        }
        // 没有备份记录时保持现状：首次启动 StatusLabel 本来是空的，
        // 用户如果在点「自动发现」，StatusLabel 里是发现结果，不要覆盖
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

        if (PowerSaveModeHelper.IsPowerSaveMode())
        {
            var cont = await DisplayAlert(
                "当前为省电模式",
                "省电模式会限制 Wi-Fi 性能，备份过程中可能出现偶发卡顿或文件上传失败。\n\n" +
                "CloudBox 会在备份期间自动持有 Wi-Fi 高性能锁缓解这个问题，" +
                "但如果网络仍然不稳定，建议关闭省电模式后再备份。",
                "继续备份", "去关闭省电");
            if (!cont)
            {
                PowerSaveModeHelper.OpenPowerSaveSettings();
                StatusLabel.Text = "请关闭省电模式后重新点击「开始扫描并备份」";
                return;
            }
        }

        Preferences.Default.Remove("last_backup_result");
        Preferences.Default.Remove("last_backup_time");
        _cancelRequested = false;

        StatusLabel.Text = "备份已启动，可息屏或切换应用";
        BackupServiceStarter.Start();
        // 不立即调 RefreshUiState：让 1 秒后的定时器采样到 running=true，
        // 把 StatusLabel 平滑切换为「备份进行中」，不需要覆盖用户刚看到的「备份已启动」
#else
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
        _cancelRequested = true;
        BackupServiceStarter.Cancel();
        StatusLabel.Text = "正在取消…";
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
            // 按钮状态同步：只刷按钮，不覆盖 StatusLabel
            RefreshUiState();
        }
#endif
    }
}