using Android;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace CloudBox.Client.Services;

public static class AndroidPermissionHelper
{
    private const int RequestCode = 1001;
    private static TaskCompletionSource<bool>? _tcs;

    public static Task<bool> RequestMediaPermissionsAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return Task.FromResult(false);

        var missing = GetMissingPermissions(activity);
        if (missing.Count == 0) return Task.FromResult(true);

        _tcs = new TaskCompletionSource<bool>();
        ActivityCompat.RequestPermissions(activity, missing.ToArray(), RequestCode);
        return _tcs.Task;
    }

    public static void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode != RequestCode) return;
        var granted = grantResults.Any(r => r == Permission.Granted);
        _tcs?.TrySetResult(granted);
        _tcs = null;
    }

    private static List<string> GetMissingPermissions(Android.App.Activity activity)
    {
        var result = new List<string>();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.ReadMediaImages) != Permission.Granted)
                result.Add(Manifest.Permission.ReadMediaImages);
            if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.ReadMediaVideo) != Permission.Granted)
                result.Add(Manifest.Permission.ReadMediaVideo);
        }
        else
        {
            if (ContextCompat.CheckSelfPermission(activity, Manifest.Permission.ReadExternalStorage) != Permission.Granted)
                result.Add(Manifest.Permission.ReadExternalStorage);
        }
        return result;
    }
}