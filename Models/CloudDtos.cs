namespace CloudBox.Client.Models;

public record PlanRequest(
    string FileHash,
    string FileName,
    long Size,
    string DeviceId,
    string? ContentType,
    DateTimeOffset FileTime);

public record PlanResponse(Guid UploadId, bool AlreadyComplete, Guid? FileId);

public record CompleteResponse(Guid FileId, bool Deduplicated);

public record HashesResponse(int Count, List<string> Hashes);

public record UploadOutcome(Guid? FileId, string FileHash, bool Skipped);

public class LocalSyncRecord
{
    public string LocalPath { get; set; } = "";
    public string FileHash { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public Guid? ServerFileId { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
}