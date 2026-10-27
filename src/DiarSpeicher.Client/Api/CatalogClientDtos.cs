namespace DiarSpeicher.Client.Api;

public sealed record RemovedResult(int Removed);

public sealed record MatchedResult(bool Matched);

public sealed record CountedUpdateResult(bool Updated, int Count, int AffectedBooks);

public sealed record SystemClaimResponse(bool IsClaimed, string? Status);

public sealed record FolderHit(string Path, string Name, string Parent, int Depth);

public sealed record FolderIndexStatus(bool Built, int Folders, string? UpdatedAt, bool Running);

public sealed record MetadataImportInitResponse(string UploadId, long ChunkSize);

public sealed record MetadataChunkResponse(long Offset);
