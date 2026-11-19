using Photos;

namespace CloudBox.Client.Platforms.iOS;

public static class IOSPermissionHelper
{
    public static async Task<bool> RequestPhotoLibraryAccessAsync()
    {
        var status = await PHPhotoLibrary.RequestAuthorizationAsync();
        return status == PHAuthorizationStatus.Authorized ||
               status == PHAuthorizationStatus.Limited;
    }
}