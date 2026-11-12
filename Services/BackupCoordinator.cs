using CloudBox.Client.Models;

namespace CloudBox.Client.Services;

public class BackupCoordinator
{
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

    public async Task<BackupResult> RunAsync(IProgress<string>? progress, CancellationToken ct)
    {
        // 清理上次进程被杀时残留的上传临时文件
        FileUploader.CleanStaleTempFiles(TimeSpan.FromHours(24));

        var items = _scanner.ScanPhotosAndVideos();
        int uploaded = 0, skipped = 0, failed = 0;

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
                await using var stream = Android.App.Application.Context
                    .ContentResolver!.OpenInputStream(Android.Net.Uri.Parse(item.ContentUri))!;

                var fileTime = item.TakenAt ?? item.LastModified;
                var fileId = await _uploader.UploadAsync(
                    stream, item.DisplayName, item.ContentType, _deviceId,
                    fileTime, item.Size, progress, ct);

                _state.Upsert(new LocalSyncRecord
                {
                    LocalPath = item.ContentUri,
                    Size = item.Size,
                    LastModified = item.LastModified,
                    ServerFileId = fileId,
                    UploadedAt = DateTimeOffset.UtcNow
                });
                uploaded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                progress?.Report($"FAILED {item.DisplayName}: {ex.Message}");
            }
        }

        return new BackupResult(items.Count, uploaded, skipped, failed);
    }
}

public record BackupResult(int Total, int Uploaded, int Skipped, int Failed);