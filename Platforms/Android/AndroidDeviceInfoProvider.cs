using Android.Provider;

namespace CloudBox.Client.Services;

public static class AndroidDeviceInfoProvider
{
    private static string GetMarketName()
    {
        // 首选属性，并准备几个厂商特定的备选属性
        var props = new[]
        {
            "ro.config.marketing_name",
            "ro.product.marketname",
            "ro.product.vendor.marketname",
            "ro.vendor.product.marketname",
            "ro.product.fullname"
        };

        foreach (var prop in props)
        {
            try
            {
                var systemPropClass = Java.Lang.Class.ForName("android.os.SystemProperties");
                var getMethod = systemPropClass.GetMethod("get", Java.Lang.Class.FromType(typeof(Java.Lang.String)));
                var value = (string?)getMethod.Invoke(null, new Java.Lang.String(prop));

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
                // 单个属性读取失败，继续尝试下一个
            }
        }

        // 所有属性都取不到时，用内部型号兜底
        return $"{DeviceInfo.Model}";
    }

    /// <summary>生成用于服务端文件夹命名的设备标识，如 "HUAWEI_P70 Pro+_a1b2c3d4"</summary>
    public static string GetDeviceFolderName()
    {
        //var manufacturer = DeviceInfo.Manufacturer ?? "Unknown";
        var model = DeviceInfo.Model ?? "Device";
        var marketName = GetMarketName() ?? model;

        // 清理非法文件名字符
        var safe = string.Join("_",
            $"{marketName}".Split(Path.GetInvalidFileNameChars()));
            //$"{manufacturer}_{marketName}".Split(Path.GetInvalidFileNameChars()));
        safe = safe.Replace(" ", "_").Trim('_');

        // AndroidId：设备+用户+App签名唯一，卸载重装不变（除非恢复出厂设置）
        var androidId = Settings.Secure.GetString(
            Android.App.Application.Context.ContentResolver,
            Settings.Secure.AndroidId) ?? "noid";

        var shortId = androidId.Length > 8 ? androidId[..8] : androidId;
        return $"{safe}_{shortId}";
    }
}