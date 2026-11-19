using Android.Content;
using Android.Provider;
using CloudBox.Core.Services;

namespace CloudBox.Client.Platforms.Android;

public class AndroidMediaScanner : IMediaScanner
{
    public Task<List<MediaItem>> ScanPhotosAndVideosAsync(CancellationToken ct = default)
    {
        var items = new List<MediaItem>();
        var ctx = global::Android.App.Application.Context;

        ScanCollection(ctx, MediaStore.Images.Media.ExternalContentUri, "image/", items, ct);
        ScanCollection(ctx, MediaStore.Video.Media.ExternalContentUri, "video/", items, ct);

        return Task.FromResult(items);
    }

    private void ScanCollection(
        Context ctx, global::Android.Net.Uri collection,
        string mimePrefix, List<MediaItem> output, CancellationToken ct)
    {
        var projection = new[]
        {
            MediaStore.MediaColumns.Id,
            MediaStore.MediaColumns.DisplayName,
            MediaStore.MediaColumns.Size,
            MediaStore.MediaColumns.DateModified,
            MediaStore.MediaColumns.MimeType,
            MediaStore.MediaColumns.DateTaken
        };

        using var cursor = ctx.ContentResolver!.Query(
            collection, projection, null, null,
            $"{MediaStore.MediaColumns.DateModified} DESC");

        if (cursor == null) return;

        var idCol = cursor.GetColumnIndexOrThrow(MediaStore.MediaColumns.Id);
        var nameCol = cursor.GetColumnIndexOrThrow(MediaStore.MediaColumns.DisplayName);
        var sizeCol = cursor.GetColumnIndexOrThrow(MediaStore.MediaColumns.Size);
        var dateCol = cursor.GetColumnIndexOrThrow(MediaStore.MediaColumns.DateModified);
        var mimeCol = cursor.GetColumnIndexOrThrow(MediaStore.MediaColumns.MimeType);
        var dateTakenCol = cursor.GetColumnIndex(MediaStore.MediaColumns.DateTaken);

        while (cursor.MoveToNext())
        {
            ct.ThrowIfCancellationRequested();

            var id = cursor.GetLong(idCol);
            var mime = cursor.GetString(mimeCol) ?? "";
            if (!mime.StartsWith(mimePrefix)) continue;

            var uri = global::Android.Net.Uri.WithAppendedPath(collection, id.ToString());

            DateTimeOffset? takenAt = null;
            if (dateTakenCol >= 0 && !cursor.IsNull(dateTakenCol))
            {
                var ms = cursor.GetLong(dateTakenCol);
                if (ms > 0)
                    takenAt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            }

            var capturedUri = uri;
            output.Add(new MediaItem
            {
                Id = uri.ToString(),
                DisplayName = cursor.GetString(nameCol) ?? $"file_{id}",
                Size = cursor.GetLong(sizeCol),
                LastModified = DateTimeOffset.FromUnixTimeSeconds(
                    cursor.GetLong(dateCol)).ToLocalTime(),
                TakenAt = takenAt,
                ContentType = mime,
                OpenStream = () => global::Android.App.Application.Context
                    .ContentResolver!.OpenInputStream(capturedUri)!
            });
        }
    }
}