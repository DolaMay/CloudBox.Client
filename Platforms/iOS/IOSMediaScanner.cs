using CloudBox.Core.Services;
using Foundation;
using Photos;

namespace CloudBox.Client.Platforms.iOS;

public class IOSMediaScanner : IMediaScanner
{
    public async Task<List<MediaItem>> ScanPhotosAndVideosAsync(CancellationToken ct = default)
    {
        var status = await PHPhotoLibrary.RequestAuthorizationAsync();
        if (status != PHAuthorizationStatus.Authorized &&
            status != PHAuthorizationStatus.Limited)
            return new List<MediaItem>();

        var items = new List<MediaItem>();

        var options = new PHFetchOptions
        {
            SortDescriptors = new[] { new NSSortDescriptor("creationDate", ascending: false) }
        };
        var assets = PHAsset.FetchAssets(options);

        assets.Enumerate(new PHFetchResultEnumerator((NSObject element, UIntPtr elementIndex, out bool stop) =>
        {
            ct.ThrowIfCancellationRequested();
            stop = false;
            if (element is not PHAsset asset) return;
            var resources = PHAssetResource.GetAssetResources(asset);
            if (resources.Length == 0) return;

            var resource = resources[0];
            var fileName = resource.OriginalFilename ?? $"asset_{elementIndex}";
            var isVideo = asset.MediaType == PHAssetMediaType.Video;

            long size = 0;
            try
            {
                // ValueForKey 返回 NSObject，需要先转成 NSNumber 才能读数值
                var sizeNum = resource.ValueForKey((NSString)"fileSize") as NSNumber;
                if (sizeNum != null)
                    size = sizeNum.LongValue.ToInt64();
            }
            catch { /* 某些资源可能取不到大小，保持 0 */ }

            var localId = asset.LocalIdentifier;
            var phAsset = asset;
            var phResource = resource;

            items.Add(new MediaItem
            {
                Id = localId,
                DisplayName = fileName,
                Size = size,
                LastModified = asset.ModificationDate is null 
                    ? DateTimeOffset.UtcNow
                    : new DateTimeOffset((DateTime)asset.ModificationDate),
                TakenAt = asset.CreationDate is null
                    ? (DateTimeOffset?)null
                    : new DateTimeOffset((DateTime)asset.CreationDate),
                ContentType = isVideo ? GetVideoMime(fileName) : GetImageMime(fileName),
                OpenStream = () => OpenAssetStream(phAsset, phResource)
            });

            return;
        }));

        return items;
    }

    /// <summary>把 PHAsset 的原始数据落到临时文件并返回 FileStream（同步阻塞，调用方在 Task 里跑）。</summary>
    private static Stream OpenAssetStream(PHAsset asset, PHAssetResource resource)
    {
        var tempPath = Path.Combine(
            CloudBox.Core.AppPaths.CacheDirectory,
            $"phasset_{Guid.NewGuid():N}_{resource.OriginalFilename}");

        var options = new PHAssetResourceRequestOptions { NetworkAccessAllowed = true };
        var tcs = new TaskCompletionSource<bool>();
        PHAssetResourceManager.DefaultManager.WriteData(
            resource, NSUrl.FromFilename(tempPath), options,
            error => tcs.TrySetResult(error == null));

        // 同步等待（调用方已在后台线程），和 Android 的 OpenInputStream 语义一致
        tcs.Task.GetAwaiter().GetResult();
        return File.OpenRead(tempPath);
    }

    private static string GetImageMime(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".heic" or ".heif" => "image/heic",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    private static string GetVideoMime(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            _ => "video/mp4"
        };
    }
}