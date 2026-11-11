#if ANDROID
using Android.Content;
using Android.Provider;
using CloudBox.Client.Models;

namespace CloudBox.Client.Services;

public class AndroidMediaScanner
{
    public List<MediaItem> ScanPhotosAndVideos()
    {
        var items = new List<MediaItem>();
        var ctx = Android.App.Application.Context;

        ScanCollection(ctx, MediaStore.Images.Media.ExternalContentUri, "image/", items);
        ScanCollection(ctx, MediaStore.Video.Media.ExternalContentUri, "video/", items);

        return items;
    }

    private void ScanCollection(Context ctx, Android.Net.Uri collection, string mimePrefix, List<MediaItem> output)
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
            var id = cursor.GetLong(idCol);
            var mime = cursor.GetString(mimeCol) ?? "";
            if (!mime.StartsWith(mimePrefix)) continue;

            var uri = Android.Net.Uri.WithAppendedPath(collection, id.ToString());
            DateTimeOffset? takenAt = null;
            if (dateTakenCol >= 0 && !cursor.IsNull(dateTakenCol))
            {
                var ms = cursor.GetLong(dateTakenCol);
                if (ms > 0)
                    takenAt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            }
            output.Add(new MediaItem
            {
                ContentUri = uri.ToString(),
                DisplayName = cursor.GetString(nameCol) ?? $"file_{id}",
                Size = cursor.GetLong(sizeCol),
                LastModified = DateTimeOffset.FromUnixTimeSeconds(cursor.GetLong(dateCol)).ToLocalTime(),
                ContentType = mime,
                TakenAt = takenAt
            });
        }
    }
}

public class MediaItem
{
    public DateTimeOffset? TakenAt { get; set; }
    public string ContentUri { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public string ContentType { get; set; } = "";
}
#endif