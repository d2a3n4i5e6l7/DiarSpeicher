namespace DiarSpeicher.Infrastructure.Data.Extensions;

internal sealed record IdNameRow(string Id, string Name);

internal sealed record IdPathRow(string Id, string Path);

internal sealed record IdThumbnailRow(string Id, string? ThumbnailPath);

internal sealed record IdPathThumbnailRow(string Id, string Path, string? ThumbnailPath);

internal sealed record MediaCountRow(string MediaId, int Count);

internal sealed record MediaTotalPagesRow(string MediaId, int TotalPages);
