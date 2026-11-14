using Android.Content;
using Android.OS;
using Microsoft.Maui.ApplicationModel;

namespace CloudBox.Client.Platforms.Android;

public static class PowerSaveModeHelper
{
    public static bool IsPowerSaveMode()
    {
        try
        {
            var ctx = Platform.AppContext;
            var pm = (PowerManager?)ctx.GetSystemService(Context.PowerService);
            return pm?.IsPowerSaveMode ?? false;
        }
        catch { return false; }
    }

    /// <summary>打开系统设置，引导用户关闭省电模式</summary>
    public static void OpenPowerSaveSettings()
    {
        var ctx = Platform.AppContext;

        // 1. 优先尝试打开厂商定制的电池设置页（华为、小米等在此页面提供省电模式开关）
        try
        {
            var intent = new Intent(Intent.ActionPowerUsageSummary);
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
            return;
        }
        catch { /* 该设备不支持 ActionPowerUsageSummary，继续尝试下一种 */ }

        // 2. 回退到原生 Android 的电池节电助手设置页（适用于原生或类原生系统）
        try
        {
            var intent = new Intent(
                global::Android.Provider.Settings.ActionBatterySaverSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
            return;
        }
        catch { /* 部分 ROM 没有此页面，最后回退到电池优化设置页 */ }

        // 3. 最终回退
        BatteryOptimizationHelper.OpenBatteryOptimizationSettings();
    }
}