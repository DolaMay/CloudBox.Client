using CloudBox.Client.Models;

namespace CloudBox.Client.Services;

public class BackupCoordinator
{
    private static readonly SemaphoreSlim _globalLock = new(1, 1);

    private readonly AndroidMediaScanner _scanner;
    private readonly SyncStateStore _state;
    private readonly FileUploader _uploader;
    private readonly string _deviceId;

    public BackupCoordinator(string baseUrl, string apiKey, string deviceId)
    {
        _scanner = new AndroidMediaScanner();
        _state = new SyncStateStore();
        _uploader = new FileUploader(baseUrl, apiKey);
        _deviceId = deviceId;
    }

    public Task<BackupResult> RunAsync(IProgress<string>? progress, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            if (!await _globalLock.WaitAsync(TimeSpan.Zero, ct))
            {
                progress?.Report("已有备份任务在运行，本次跳过");
                return new BackupResult(0, 0, 0, 0);
            }

            try { return await RunInternalAsync(progress, ct); }
            finally { _globalLock.Release(); }
        }, ct);
    }

    private async Task<BackupResult> RunInternalAsync(
        IProgress<string>? progress, CancellationToken ct)
    {
        FileUploader.CleanStaleTempFiles(TimeSpan.FromHours(24));

        var items = _scanner.ScanPhotosAndVideos();
        var photoCount = items.Count(i => i.ContentType?.StartsWith("image/") == true);
        var videoCount = items.Count(i => i.ContentType?.StartsWith("video/") == true);
        progress?.Report($"扫描到 {items.Count} 项（{photoCount} 照片, {videoCount} 视频）");

        var serverIndex = await _uploader.GetServerIndexAsync(progress, ct);

        int uploaded = 0, skipped = 0, failed = 0;

        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                var existing = _state.Get(item.ContentUri);
                if (existing != null && existing.LastModified == item.LastModified)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var fileTime = item.TakenAt ?? item.LastModified;
                    var contentUri = Android.Net.Uri.Parse(item.ContentUri);

                    var outcome = await _uploader.UploadAsync(
                        () => Android.App.Application.Context.ContentResolver!
                                .OpenInputStream(contentUri)!,
                        item.DisplayName, item.ContentType, _deviceId,
                        fileTime, item.LastModified, item.Size,
                        serverIndex, progress, ct);

                    _state.Upsert(new LocalSyncRecord
                    {
                        LocalPath = item.ContentUri,
                        FileHash = outcome.FileHash,
                        Size = item.Size,
                        LastModified = item.LastModified,
                        ServerFileId = outcome.FileId,
                        UploadedAt = DateTimeOffset.UtcNow
                    });

                    if (outcome.Skipped) skipped++;
                    else uploaded++;
                }
                catch (Exception ex) when (ex is not System.OperationCanceledException)
                {
                    failed++;
                    progress?.Report($"失败 {item.DisplayName}: {ex.Message}");
                }
            }
        }
        finally
        {
            _state.Flush();
        }

        return new BackupResult(items.Count, uploaded, skipped, failed);
    }
}

public record BackupResult(int Total, int Uploaded, int Skipped, int Failed);