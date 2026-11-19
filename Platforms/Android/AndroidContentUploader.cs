using System.Net;
using System.Net.Http.Headers;
using CloudBox.Core.Services;

namespace CloudBox.Client.Platforms.Android;

public class AndroidContentUploader : IContentUploader
{
    private readonly HttpClient _client;

    public AndroidContentUploader()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromSeconds(15),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AutomaticDecompression = DecompressionMethods.All,
        };
        _client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(25) };
    }

    public async Task UploadContentAsync(
        string baseUrl, string apiKey, Guid uploadId,
        string localFilePath, long totalBytes,
        IProgress<string>? progress, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(60) +
                      TimeSpan.FromSeconds(totalBytes / 1024 / 1024 * 2);
        if (timeout > TimeSpan.FromMinutes(20)) timeout = TimeSpan.FromMinutes(20);

        using var contentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        contentCts.CancelAfter(timeout);

        using var fs = File.OpenRead(localFilePath);
        using var content = new StreamContent(fs, 81920);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Headers.ContentLength = totalBytes;

        using var req = new HttpRequestMessage(
            HttpMethod.Put, $"{baseUrl.TrimEnd('/')}/api/upload/{uploadId}/content")
        { Content = content };
        req.Headers.Add("X-Api-Key", apiKey);

        using var resp = await _client.SendAsync(req, contentCts.Token);
        resp.EnsureSuccessStatusCode();
    }
}