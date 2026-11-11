namespace CloudBox.Client.Models;

public record PlanRequest(
    string FileHash,
    string FileName,
    long Size,
    int ChunkSize,
    List<string> ChunkHashes,
    string DeviceId,
    string? ContentType,
    DateTimeOffset FileTime);

public record PlanResponse(Guid UploadId, List<int> MissingChunks, bool AlreadyComplete, Guid? FileId);

public record CompleteResponse(Guid FileId, bool Deduplicated);

public class LocalSyncRecord
{
    public string LocalPath { get; set; } = "";
    public string FileHash { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public Guid? ServerFileId { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
}