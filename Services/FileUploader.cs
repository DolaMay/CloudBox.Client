using System.Net;
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

    private static readonly TimeSpan ShortRequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ContentUploadTimeout = TimeSpan.FromMinutes(20);

    public FileUploader(string baseUrl, string apiKey)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromSeconds(15),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = false,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(25),
        };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
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
        using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        reqCts.CancelAfter(ShortRequestTimeout);

        try
        {
            using var resp = await _http.GetAsync("/api/files/hashes", reqCts.Token);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(reqCts.Token);
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

    public async Task<UploadOutcome> UploadAsync(
        Func<Stream> openSource,
        string fileName, string contentType,
        string deviceId, DateTimeOffset fileTime, DateTimeOffset lastModified,
        long expectedSize,
        ServerFileIndex? serverIndex,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (serverIndex != null &&
            serverIndex.ContainsByMetadata(fileName, expectedSize, lastModified))
        {
            progress?.Report($"  已在服务端（元数据匹配）: {fileName}");
            return new UploadOutcome(null, "", Skipped: true);
        }

        // 阶段 1：算 hash
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

        // 阶段 2：写临时文件 + 上传
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

            // ---- plan ----
            PlanResponse planResp;
            try
            {
                planResp = await PostPlanWithRetryAsync(planReq, progress, ct);
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;  // 用户取消，继续传播
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"plan failed for {fileName}: {ex.Message}", ex);
            }

            if (planResp.AlreadyComplete)
            {
                if (!planResp.FileId.HasValue)
                    throw new InvalidOperationException("server returned alreadyComplete but no fileId");
                progress?.Report($"  服务端已存在: {fileName}");
                return new UploadOutcome(planResp.FileId.Value, fileHash, Skipped: true);
            }

            // ---- content（关键：过滤内部 OCE） ----
            try
            {
                using var contentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var timeout = TimeSpan.FromSeconds(60) +
                              TimeSpan.FromSeconds(totalBytes / 1024 / 1024 * 2);
                if (timeout > ContentUploadTimeout) timeout = ContentUploadTimeout;
                contentCts.CancelAfter(timeout);

                await using (var fs = new FileStream(localPath, FileMode.Open,
                                 FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                {
                    using var content = new StreamContent(fs, 81920);
                    content.Headers.ContentType = new MediaTypeHeaderValue(
                        string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);
                    content.Headers.ContentLength = totalBytes;

                    using var resp = await _http.PutAsync(
                        $"/api/upload/{planResp.UploadId}/content", content, contentCts.Token);
                    resp.EnsureSuccessStatusCode();
                }
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;  // 用户取消
            }
            catch (System.OperationCanceledException)
            {
                // 内部 content CT 超时 → 转换为 TimeoutException
                throw new TimeoutException($"content upload timeout for {fileName}");
            }
            catch (IOException ioex)
            {
                throw new IOException($"content upload I/O error for {fileName}: {ioex.Message}", ioex);
            }
            progress?.Report($"  已上传 {totalBytes} 字节: {fileName}");

            // ---- complete ----
            CompleteResponse completeResp;
            try
            {
                completeResp = await PostCompleteWithRetryAsync(planResp.UploadId, progress, ct);
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"complete failed for {fileName}: {ex.Message}", ex);
            }

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

    private async Task<PlanResponse> PostPlanWithRetryAsync(
        PlanRequest req, IProgress<string>? progress, CancellationToken ct)
    {
        Exception? lastEx = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(ShortRequestTimeout);

            try
            {
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/api/upload/plan");
                httpReq.Content = new StringContent(
                    JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");

                using var resp = await _http.SendAsync(httpReq, reqCts.Token);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(reqCts.Token);
                return JsonSerializer.Deserialize<PlanResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;  // 用户取消，穿透
            }
            catch (System.OperationCanceledException)
            {
                lastEx = new TimeoutException($"plan timeout after {ShortRequestTimeout.TotalSeconds}s");
                if (attempt == 0) progress?.Report("  plan 超时，重试中...");
            }
            catch (HttpRequestException ex)
            {
                lastEx = ex;
                if (attempt == 0) progress?.Report($"  plan 网络错误，重试中... ({ex.Message})");
            }
        }

        throw lastEx ?? new InvalidOperationException("plan failed");
    }

    private async Task<CompleteResponse> PostCompleteWithRetryAsync(
        Guid uploadId, IProgress<string>? progress, CancellationToken ct)
    {
        Exception? lastEx = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var reqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reqCts.CancelAfter(ShortRequestTimeout);

            try
            {
                using var httpReq = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/upload/{uploadId}/complete");

                using var resp = await _http.SendAsync(httpReq, reqCts.Token);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(reqCts.Token);
                return JsonSerializer.Deserialize<CompleteResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }
            catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (System.OperationCanceledException)
            {
                lastEx = new TimeoutException($"complete timeout after {ShortRequestTimeout.TotalSeconds}s");
                if (attempt == 0) progress?.Report("  complete 超时，重试中...");
            }
            catch (HttpRequestException ex)
            {
                lastEx = ex;
                if (attempt == 0) progress?.Report($"  complete 网络错误，重试中... ({ex.Message})");
            }
        }

        throw lastEx ?? new InvalidOperationException("complete failed");
    }
}