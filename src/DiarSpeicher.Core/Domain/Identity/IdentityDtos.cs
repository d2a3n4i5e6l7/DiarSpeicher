namespace DiarSpeicher.Core.Domain.Identity;

public record ClaimServerRequest(string Username, string Password);

public record CreateUserRequest(string Username, string Password, bool IsServerOwner);

public record UpdateUserRequest(string? Password, bool? IsLocked, int? MaxSessionsAllowed);

public record CreateApiKeyRequest(string Name, DateTimeOffset? ExpiresAt);

public record UserDto(
    string Id,
    string Username,
    bool IsServerOwner,
    bool IsLocked,
    DateTimeOffset CreatedAt);

public record ApiKeyDto(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Returned only at issue time: <see cref="Key"/> is the one and only time the plaintext
/// key is available, since the store keeps just its hash.
/// </summary>
public record CreatedApiKeyDto(
    string Id,
    string Name,
    string Key,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public enum IdentityErrorCode
{
    None,
    AlreadyClaimed,
    UsernameTaken,
    UserNotFound,
    InvalidRequest
}

public record IdentityResult<T>(T? Value, IdentityErrorCode Error, string? Message = null)
{
    public bool Succeeded => Error == IdentityErrorCode.None;

    public static IdentityResult<T> Ok(T value) => new(value, IdentityErrorCode.None);

    public static IdentityResult<T> Fail(IdentityErrorCode error, string message) => new(default, error, message);
}
