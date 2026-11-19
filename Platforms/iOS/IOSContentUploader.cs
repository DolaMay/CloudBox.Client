using CloudBox.Core.Services;
using Foundation;
using System.Net.Http.Headers;

namespace CloudBox.Client.Platforms.iOS;

public class IOSContentUploader : IContentUploader
{
    private readonly HttpClient _client;

    public IOSContentUploader()
    {
        var config = NSUrlSessionConfiguration.CreateBackgroundSessionConfiguration(
            "com.companyname.cloudbox.upload");
        config.TimeoutIntervalForRequest = 20;
        config.TimeoutIntervalForResource = 1800;
        config.Discretionary = false;

        _client = new HttpClient(new NSUrlSessionHandler(config))
        {
            Timeout = TimeSpan.FromMinutes(25),
        };
    }

    public async Task UploadContentAsync(
        string baseUrl, string apiKey, Guid uploadId,
        string localFilePath, long totalBytes,
        IProgress<string>? progress, CancellationToken ct)
    {
        using var fs = File.OpenRead(localFilePath);
        using var content = new StreamContent(fs, 81920);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = totalBytes;

        using var req = new HttpRequestMessage(
            HttpMethod.Put, $"{baseUrl.TrimEnd('/')}/api/upload/{uploadId}/content")
        { Content = content };
        req.Headers.Add("X-Api-Key", apiKey);

        using var resp = await _client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}