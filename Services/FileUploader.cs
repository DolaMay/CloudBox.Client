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

    /// <summary>
    /// 清理 .upload_tmp 下超过 maxAge 的残留文件（上次进程被杀时的遗留）。
    /// 每次备份任务启动时调用一次。
    /// </summary>
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
                    if (fi.LastWriteTimeUtc < cutoffUtc)
                        fi.Delete();
                }
                catch { /* 单个文件删不掉不影响其它 */ }
            }

            if (!Directory.EnumerateFileSystemEntries(uploadTempDir).Any())
                Directory.Delete(uploadTempDir);
        }
        catch { /* 整个目录被占用之类，忽略 */ }
    }

    public async Task<Guid> UploadAsync(
        Stream source, string fileName, string contentType,
        string deviceId, DateTimeOffset fileTime, long expectedSize,
        IProgress<string>? progress, CancellationToken ct)
    {
        // 1) 用 AppDataDirectory 下的专用临时目录，而不是 CacheDirectory
        var uploadTempDir = Path.Combine(FileSystem.AppDataDirectory, TempDirName);
        Directory.CreateDirectory(uploadTempDir);
        var localPath = Path.Combine(uploadTempDir, $"{Guid.NewGuid():N}.upload");

        try
        {
            // 2) 磁盘空间预检查
            if (expectedSize > 0)
            {
                try
                {
                    var driveInfo = new DriveInfo(
                        Path.GetPathRoot(FileSystem.AppDataDirectory)!);
                    if (driveInfo.AvailableFreeSpace < expectedSize)
                        throw new IOException(
                            $"Not enough free space. Need {expectedSize / 1024 / 1024} MB, " +
                            $"available {driveInfo.AvailableFreeSpace / 1024 / 1024} MB.");
                }
                catch (ArgumentException)
                {
                    // GetPathRoot 在某些 Android 环境下可能抛异常或返回 null，
                    // 拿不到 DriveInfo 就跳过检查，让写入时自然暴露 ENOSPC
                }
            }

            // 3) 流式写入临时文件并计算整文件 SHA-256
            string fileHash;
            long totalBytes = 0;

            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var fs = new FileStream(localPath, FileMode.Create,
                             FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    totalBytes += read;
                }
                fileHash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }

            if (expectedSize > 0 && totalBytes != expectedSize)
                throw new InvalidOperationException(
                    $"size mismatch: expected {expectedSize}, got {totalBytes}");

            progress?.Report($"plan: {fileName} ({totalBytes} bytes)");

            // 4) plan
            var planReq = new PlanRequest(
                fileHash, fileName, totalBytes, deviceId, contentType, fileTime);
            var planResp = await PostJsonAsync<PlanResponse>("/api/upload/plan", planReq, ct);

            if (planResp.AlreadyComplete)
            {
                if (!planResp.FileId.HasValue)
                    throw new InvalidOperationException("server returned alreadyComplete but no fileId");
                progress?.Report($"  dedup: {fileName} already on server");
                return planResp.FileId.Value;
            }

            // 5) PUT 整个文件
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
            progress?.Report($"  uploaded {totalBytes} bytes");

            // 6) complete
            var completeResp = await PostJsonAsync<CompleteResponse>(
                $"/api/upload/{planResp.UploadId}/complete", null, ct);
            return completeResp.FileId;
        }
        finally
        {
            // 7) 无论成功失败，都删临时文件；目录空了顺手删目录
            if (File.Exists(localPath))
            {
                try { File.Delete(localPath); } catch { }
            }
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