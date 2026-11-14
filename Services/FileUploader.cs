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

    /// <summary>拉取服务端已上传完成的所有 fileHash</summary>
    public async Task<HashSet<string>> GetServerHashesAsync(
        IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync("/api/files/hashes", ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<HashesResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var set = new HashSet<string>(parsed?.Hashes ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            progress?.Report($"server hashes: {set.Count}");
            return set;
        }
        catch (Exception ex)
        {
            progress?.Report($"hashes fetch failed (continuing without): {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<UploadOutcome> UploadAsync(
        Stream source, string fileName, string contentType,
        string deviceId, DateTimeOffset fileTime, long expectedSize,
        HashSet<string>? serverHashes,
        IProgress<string>? progress, CancellationToken ct)
    {
        var uploadTempDir = Path.Combine(FileSystem.AppDataDirectory, TempDirName);
        Directory.CreateDirectory(uploadTempDir);
        var localPath = Path.Combine(uploadTempDir, $"{Guid.NewGuid():N}.upload");

        try
        {
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

            // 服务端已有 → 不调 plan，直接返回
            if (serverHashes != null && serverHashes.Contains(fileHash))
            {
                //progress?.Report($"  skip (on server): {fileName}");
                return new UploadOutcome(null, fileHash, Skipped: true);
            }

            progress?.Report($"plan: {fileName} ({totalBytes} bytes)");

            var planReq = new PlanRequest(
                fileHash, fileName, totalBytes, deviceId, contentType, fileTime);
            var planResp = await PostJsonAsync<PlanResponse>("/api/upload/plan", planReq, ct);

            if (planResp.AlreadyComplete)
            {
                if (!planResp.FileId.HasValue)
                    throw new InvalidOperationException("server returned alreadyComplete but no fileId");
                progress?.Report($"  dedup: {fileName}");
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
            progress?.Report($"  uploaded {totalBytes} bytes");

            var completeResp = await PostJsonAsync<CompleteResponse>(
                $"/api/upload/{planResp.UploadId}/complete", null, ct);
            return new UploadOutcome(completeResp.FileId, fileHash, Skipped: false);
        }
        finally
        {
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