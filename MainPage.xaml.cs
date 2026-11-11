using System.Collections.ObjectModel;
using CloudBox.Client.Services;

namespace CloudBox.Client;

public partial class MainPage : ContentPage
{
    private CancellationTokenSource? _cts;

    public MainPage() => InitializeComponent();

    private async void OnBackupClicked(object sender, EventArgs e)
    {
        var baseUrl = ServerUrlEntry.Text?.Trim();
        var apiKey = ApiKeyEntry.Text?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
        {
            StatusLabel.Text = "请填写服务器地址和 API Key";
            return;
        }

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