namespace DiarSpeicher.Core.Domain.MangaBaka;

public sealed record MetadataImportInitRequest(string FileName, long TotalBytes);

public sealed record MetadataImportCompleteRequest(string UploadId, string FileName);
