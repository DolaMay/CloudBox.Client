using Android;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace CloudBox.Client.Services;

public static class AndroidPermissionHelper
{
    private const int RequestCode = 1001;
    private static TaskCompletionSource<bool>? _tcs;

    /// <summary>
    /// 请求媒体读取权限，返回 (照片权限是否授予, 视频权限是否授予)。
    /// Android 13+ 分别请求 READ_MEDIA_IMAGES / READ_MEDIA_VIDEO；
    /// Android 12- 请求 READ_EXTERNAL_STORAGE，两项返回值相同。
    /// </summary>
    public static async Task<(bool imagesGranted, bool videosGranted)> RequestMediaPermissionsAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return (false, false);

        var missing = GetMissingPermissions(activity);
        if (missing.Count > 0)
        {
            _tcs = new TaskCompletionSource<bool>();
            ActivityCompat.RequestPermissions(activity, missing.ToArray(), RequestCode);
            await _tcs.Task;
        }

        // 请求流程结束后，重新检查两个权限的实际授予状态
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var images = ContextCompat.CheckSelfPermission(
                activity, Manifest.Permission.ReadMediaImages) == Permission.Granted;
            var videos = ContextCompat.CheckSelfPermission(
                activity, Manifest.Permission.ReadMediaVideo) == Permission.Granted;
            return (images, videos);
        }
        else
        {
            var granted = ContextCompat.CheckSelfPermission(
                activity, Manifest.Permission.ReadExternalStorage) == Permission.Granted;
            return (granted, granted);
        }
    }

    /// <summary>只查询当前状态，不弹权限对话框</summary>
    public static (bool imagesGranted, bool videosGranted) CheckMediaPermissions()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return (false, false);

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var images = ContextCompat.CheckSelfPermission(
                activity, Manifest.Permission.ReadMediaImages) == Permission.Granted;
            var videos = ContextCompat.CheckSelfPermission(
                activity, Manifest.Permission.ReadMediaVideo) == Permission.Granted;
            return (images, videos);
        }
        else
        {
            var granted = ContextCompat.CheckSelfPermission(
                activity, Manifest.Permission.ReadExternalStorage) == Permission.Granted;
            return (granted, granted);
        }
    }

    public static void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode != RequestCode) return;
        // 只要系统回调了，就唤醒等待方；具体授予状态由请求方重新 Check 得到
        _tcs?.TrySetResult(true);
        _tcs = null;
    }

    private static List<string> GetMissingPermissions(Android.App.Activity activity)
    {
        var result = new List<string>();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (ContextCompat.CheckSelfPermission(
                    activity, Manifest.Permission.ReadMediaImages) != Permission.Granted)
                result.Add(Manifest.Permission.ReadMediaImages);
            if (ContextCompat.CheckSelfPermission(
                    activity, Manifest.Permission.ReadMediaVideo) != Permission.Granted)
                result.Add(Manifest.Permission.ReadMediaVideo);
        }
        else
        {
            if (ContextCompat.CheckSelfPermission(
                    activity, Manifest.Permission.ReadExternalStorage) != Permission.Granted)
                result.Add(Manifest.Permission.ReadExternalStorage);
        }
        return result;
    }
}