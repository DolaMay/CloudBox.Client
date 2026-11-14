using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudBox.Client.Models;

namespace CloudBox.Client.Services;

public class FileUploader
{
    private readonly HttpClient _http;
    private const string TempDirName = ".upload_tmp";

    public FileUploader(string baseUrl, string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        _http.Timeout = TimeSpan.FromMinutes(60);
    }

    public static void CleanStaleTempFiles(TimeSpan maxAge)
    {
        var uploadTempDir = Path.Combine(FileSystem.AppDataDirectory, TempDirName);
        if (!Directory.Exists(uploadTempDir)) return;

        var cutoffUtc = (DateTimeOffset.UtcNow - maxAge).UtcDateTime;
        try
        {
            foreach (var file in Directory.EnumerateFiles(uploadTempDir))
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (fi.LastWriteTimeUtc < cutoffUtc) fi.Delete();
                }
                catch { }
            }
            if (!Directory.EnumerateFileSystemEntries(uploadTempDir).Any())
                Directory.Delete(uploadTempDir);
        }
        catch { }
    }

    public async Task<ServerFileIndex> GetServerIndexAsync(
        IProgress<string>? progress, CancellationToken ct)
    {
        var index = new ServerFileIndex();
        try
        {
            using var resp = await _http.GetAsync("/api/files/hashes", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<HashesResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed?.Items != null)
            {
                foreach (var e in parsed.Items)
                    index.Add(e.FileName, e.Size, e.LastModified, e.FileHash);
            }
            progress?.Report($"服务端已有 {parsed?.Count ?? 0} 个文件");
        }
        catch (Exception ex)
        {
            progress?.Report($"获取服务端索引失败（继续尝试逐个上传）: {ex.Message}");
        }
        return index;
    }

    /// <summary>
    /// 上传一个文件。openSource 是流工厂，可能需要调用 0~2 次：
    /// - 元数据命中（fileName+size+lastModified）→ 调用 0 次（完全跳过）
    /// - 元数据不命中但 hash 命中 → 调用 1 次（只读流算 hash）
    /// - 都不命中 → 调用 2 次（算 hash + 复制到临时文件上传）
    /// </summary>
    public async Task<UploadOutcome> UploadAsync(
        Func<Stream> openSource,
        string fileName, string contentType,
        string deviceId, DateTimeOffset fileTime, DateTimeOffset lastModified,
        long expectedSize,
        ServerFileIndex? serverIndex,
        IProgress<string>? progress, CancellationToken ct)
    {
        // ---- 阶段 0：元数据快速匹配，不打开文件、不算 hash ----
        if (serverIndex != null &&
            serverIndex.ContainsByMetadata(fileName, expectedSize, lastModified))
        {
            progress?.Report($"  已在服务端（元数据匹配）: {fileName}");
            return new UploadOutcome(null, "", Skipped: true);
        }

        // ---- 阶段 1：只读流算 hash ----
        string fileHash;
        long totalBytes = 0;

        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            await using var source = openSource();
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                totalBytes += read;
            }
            fileHash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }

        if (expectedSize > 0 && totalBytes != expectedSize)
            throw new InvalidOperationException(
                $"size mismatch: expected {expectedSize}, got {totalBytes}");

        if (serverIndex != null && serverIndex.ContainsByHash(fileHash))
        {
            progress?.Report($"  已在服务端（hash 匹配）: {fileName}");
            return new UploadOutcome(null, fileHash, Skipped: true);
        }

        // ---- 阶段 2：写入临时文件并上传 ----
        var uploadTempDir = Path.Combine(FileSystem.AppDataDirectory, TempDirName);
        Directory.CreateDirectory(uploadTempDir);
        var localPath = Path.Combine(uploadTempDir, $"{Guid.NewGuid():N}.upload");

        try
        {
            await using (var source = openSource())
            await using (var fs = new FileStream(localPath, FileMode.Create,
                             FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(fs, 81920, ct);
            }

            progress?.Report($"plan: {fileName} ({totalBytes} bytes)");

            var planReq = new PlanRequest(
                fileHash, fileName, totalBytes, deviceId, contentType,
                fileTime, lastModified);
            var planResp = await PostJsonAsync<PlanResponse>("/api/upload/plan", planReq, ct);

            if (planResp.AlreadyComplete)
            {
                if (!planResp.FileId.HasValue)
                    throw new InvalidOperationException("server returned alreadyComplete but no fileId");
                progress?.Report($"  服务端已存在: {fileName}");
                return new UploadOutcome(planResp.FileId.Value, fileHash, Skipped: true);
            }

            await using (var fs = new FileStream(localPath, FileMode.Open,
                             FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                using var content = new StreamContent(fs, 81920);
                content.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);
                content.Headers.ContentLength = totalBytes;

                using var resp = await _http.PutAsync(
                    $"/api/upload/{planResp.UploadId}/content", content, ct);
                resp.EnsureSuccessStatusCode();
            }
            progress?.Report($"  已上传 {totalBytes} 字节: {fileName}");

            var completeResp = await PostJsonAsync<CompleteResponse>(
                $"/api/upload/{planResp.UploadId}/complete", null, ct);
            return new UploadOutcome(completeResp.FileId, fileHash, Skipped: false);
        }
        finally
        {
            try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
            try
            {
                if (Directory.Exists(uploadTempDir) &&
                    !Directory.EnumerateFileSystemEntries(uploadTempDir).Any())
                    Directory.Delete(uploadTempDir);
            }
            catch { }
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