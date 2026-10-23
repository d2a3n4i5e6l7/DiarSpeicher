using DiarSpeicher.Api.Serialization;

namespace DiarSpeicher.Api.Endpoints;

public sealed record ApiError(string Error);

public sealed record OffsetConflictError(string Error, long CurrentOffset);

public sealed record ServiceHealthResponse(string Status, string Service, string Version, string Database);

public sealed record HealthResponse(string Status);

public sealed record VersionResponse(string Semver, string Rev);

public sealed record ClaimStatusResponse(bool IsClaimed);

public sealed record CurrentUserResponse(string Id, string Username, bool IsServerOwner, int? AgeRestriction);

public sealed record UpdatedResponse(bool Updated);

public sealed record MatchedResponse(bool Matched);

public sealed record RemovedResponse(int Removed);

public sealed record EpubProfilesUpdatedResponse(bool Updated, int Count, int AffectedBooks);

public sealed record ChunkedUploadSessionResponse(string UploadId, int ChunkSize);

public sealed record UploadOffsetResponse(long Offset);

public sealed record KoboInitializationResponse(Dictionary<string, object> Resources);

public sealed record KomgaAgeRestrictionResponse(int Age, string Restriction);

public sealed record KomgaCurrentUserResponse(
    string Id,
    string Email,
    List<string> Roles,
    bool SharedAllLibraries,
    string[] SharedLibrariesIds,
    string[] LabelsAllow,
    string[] LabelsExclude,
    KomgaAgeRestrictionResponse? AgeRestriction);

internal static class ApiResults
{
    public static IResult Error(string message, int statusCode) =>
        Results.Json(new ApiError(message), ApiJsonContext.Default.ApiError, statusCode: statusCode);
}
