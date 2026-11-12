using Android.Content;
using Android.OS;
using Android.Provider;
using Microsoft.Maui.ApplicationModel;

namespace CloudBox.Client.Platforms.Android;

public static class BatteryOptimizationHelper
{
    /// <summary>是否已在电池优化白名单（返回 true = 系统不会限制后台）</summary>
    public static bool IsIgnoringBatteryOptimizations()
    {
        var ctx = Platform.AppContext;
        var pm = (PowerManager?)ctx.GetSystemService(Context.PowerService);
        return pm?.IsIgnoringBatteryOptimizations(ctx.PackageName!) ?? true;
    }

    /// <summary>打开系统的“电池优化”设置页，让用户手动把本 App 设为“不限制”</summary>
    public static void OpenBatteryOptimizationSettings()
    {
        var ctx = Platform.AppContext;

        // 优先：直接跳到电池优化列表页（不需要 REQUEST_IGNORE_BATTERY_OPTIMIZATIONS 权限）
        try
        {
            var intent = new Intent(Settings.ActionIgnoreBatteryOptimizationSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
            return;
        }
        catch { /* 部分 ROM 不支持，走回退 */ }

        // 回退：跳到本 App 的应用详情页，用户手动进“电池”项
        try
        {
            var intent = new Intent(Settings.ActionApplicationDetailsSettings);
            intent.SetData(global::Android.Net.Uri.Parse("package:" + ctx.PackageName));
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        catch { /* 实在打不开就算了，不抛异常 */ }
    }
}