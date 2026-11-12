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
        // 1) 落到缓存文件（只读一次源流）
        var tempPath = Path.Combine(FileSystem.CacheDirectory,
            $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}");
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create,
                            FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(fs, ct);
            }

            var fileSize = new FileInfo(tempPath).Length;
            if (fileSize == 0)
                throw new InvalidOperationException("empty file");

            // 2) 两遍读：第一遍算整文件哈希 + 分块哈希
            var hashes = new List<string>();
            string fileHash;
            using (var sha = SHA256.Create())
            await using (var fs = new FileStream(tempPath, FileMode.Open,
                            FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                var buffer = new byte[ChunkSize];
                int read;
                while ((read = await fs.ReadAsync(buffer.AsMemory(0, ChunkSize), ct)) > 0)
                {
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    var chunkHash = Convert.ToHexString(
                        SHA256.HashData(buffer.AsSpan(0, read))).ToLowerInvariant();
                    hashes.Add(chunkHash);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                fileHash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            }

            progress?.Report($"plan: {fileName} ({fileSize} bytes, {hashes.Count} chunks)");

            // 3) plan
            var planReq = new PlanRequest(
                fileHash, fileName, fileSize, ChunkSize, hashes, deviceId, contentType, fileTime);
            var planResp = await PostJsonAsync<PlanResponse>("/api/upload/plan", planReq, ct);
            if (planResp.AlreadyComplete) return planResp.FileId!.Value;

            // 4) 第二遍读：只上传缺失块
            foreach (var idx in planResp.MissingChunks)
            {
                var offset = (long)idx * ChunkSize;
                var len = (int)Math.Min(ChunkSize, fileSize - offset);
                var seg = new byte[len];

                await using (var fs = new FileStream(tempPath, FileMode.Open,
                                FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    var totalRead = 0;
                    while (totalRead < len)
                    {
                        var n = await fs.ReadAsync(seg.AsMemory(totalRead, len - totalRead), ct);
                        if (n == 0) break;
                        totalRead += n;
                    }
                }

                var url = $"/api/upload/{planResp.UploadId}/chunk/{idx}?sha256={hashes[idx]}";
                using var content = new ByteArrayContent(seg);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var resp = await _http.PutAsync(url, content, ct);
                resp.EnsureSuccessStatusCode();
                progress?.Report($"  chunk {idx}/{hashes.Count - 1} uploaded");
            }

            // 5) complete
            var completeResp = await PostJsonAsync<CompleteResponse>(
                $"/api/upload/{planResp.UploadId}/complete", null, ct);
            return completeResp.FileId;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
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