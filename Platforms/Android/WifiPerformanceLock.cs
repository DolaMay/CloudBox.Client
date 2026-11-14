using Android.Content;
using Android.Net.Wifi;
using Microsoft.Maui.ApplicationModel;
using Android.Net.Wifi;
using Android.Net;

namespace CloudBox.Client.Platforms.Android;

/// <summary>
/// 备份期间持有 WifiLock，让 Wi-Fi 芯片保持高性能模式，
/// 规避省电模式下的网络抖动（No route to host、plan 超时）。
/// </summary>
public static class WifiPerformanceLock
{
    private static WifiManager.WifiLock? _lock;

    public static void Acquire()
    {
        if (_lock?.IsHeld == true) return;

        try
        {
            var ctx = Platform.AppContext;
            var wifi = (WifiManager?)ctx.GetSystemService(Context.WifiService);
            if (wifi == null) return;

            // WIFI_MODE_FULL_HIGH_PERF：保持高性能，不因屏幕关闭降频
            // 注意：比 FULL 更耗电，但备份期间短暂持有可接受
            _lock = wifi.CreateWifiLock(WifiMode.FullHighPerf, "CloudBoxBackupWifiLock");
            _lock.SetReferenceCounted(false);
            _lock.Acquire();
        }
        catch
        {
            // 权限缺失或系统拒绝，降级为不带锁运行（功能不受影响，只是可能偶发卡顿）
        }
    }

    public static void Release()
    {
        try
        {
            if (_lock?.IsHeld == true)
                _lock.Release();
            _lock?.Dispose();
        }
        catch { }
        finally
        {
            _lock = null;
        }
    }
}