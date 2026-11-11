using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudBox.Client.Models;

namespace CloudBox.Client.Services;

public class ChunkUploader
{
    private readonly HttpClient _http;
    private const int ChunkSize = 4 * 1024 * 1024;

    public ChunkUploader(string baseUrl, string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        _http.Timeout = TimeSpan.FromMinutes(30);
    }

    public async Task<Guid> UploadAsync(
        Stream source, string fileName, string contentType,
        string deviceId, DateTimeOffset fileTime,
        IProgress<string>? progress, CancellationToken ct)
    {
        // 1) 读入内存（个人照片视频够用；超大视频可改为临时文件）
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var hashes = new List<string>();
        var fileHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        for (var offset = 0; offset < bytes.Length; offset += ChunkSize)
        {
            var len = Math.Min(ChunkSize, bytes.Length - offset);
            var seg = new byte[len];
            Array.Copy(bytes, offset, seg, 0, len);
            hashes.Add(Convert.ToHexString(SHA256.HashData(seg)).ToLowerInvariant());
        }

        progress?.Report($"plan: {fileName} ({bytes.Length} bytes, {hashes.Count} chunks)");

        // 2) plan
        var planReq = new PlanRequest(
            fileHash, fileName, bytes.Length, ChunkSize, hashes, deviceId, contentType, fileTime);
        var planResp = await PostJsonAsync<PlanResponse>("/api/upload/plan", planReq, ct);

        if (planResp.AlreadyComplete)
            return planResp.FileId!.Value;

        // 3) 只上传缺失的块
        foreach (var idx in planResp.MissingChunks)
        {
            var offset = idx * ChunkSize;
            var len = Math.Min(ChunkSize, bytes.Length - offset);
            var seg = new byte[len];
            Array.Copy(bytes, offset, seg, 0, len);

            var url = $"/api/upload/{planResp.UploadId}/chunk/{idx}?sha256={hashes[idx]}";
            using var content = new ByteArrayContent(seg);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var resp = await _http.PutAsync(url, content, ct);
            resp.EnsureSuccessStatusCode();
            progress?.Report($"  chunk {idx}/{hashes.Count - 1} uploaded");
        }

        // 4) complete
        var completeResp = await PostJsonAsync<CompleteResponse>(
            $"/api/upload/{planResp.UploadId}/complete", null, ct);

        return completeResp.FileId;
    }

    private async Task<T> PostJsonAsync<T>(string url, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}