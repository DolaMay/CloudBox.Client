using UIKit;

namespace CloudBox.Client.Platforms.iOS;

public static class IOSDeviceInfoProvider
{
    public static string GetDeviceFolderName()
    {
        var name = UIDevice.CurrentDevice.Name ?? "iPhone";
        var model = UIDevice.CurrentDevice.Model ?? "iPhone";

        var safe = string.Join("_", $"{name}_{model}".Split(Path.GetInvalidFileNameChars()))
            .Replace(" ", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(safe)) safe = "iPhone";

        var id = UIDevice.CurrentDevice.IdentifierForVendor?.AsString() ?? "noid";
        var shortId = id.Length > 8 ? id[..8] : id;

        return $"{safe}_{shortId}";
    }
}