using System.Text.Json.Serialization;

namespace DiarSpeicher.Client.Api;

public sealed record ApiErrorBody(string? Error, string? Message, string? Details);

public sealed record LoginRequest(
    string Username,
    string Password,
    [property: JsonPropertyName("client_pubkey")] string? ClientPubkey);

public sealed record SessionUser(
    int Id,
    string Username,
    string? Role,
    [property: JsonPropertyName("is_admin"), JsonConverter(typeof(NumericBoolConverter))] bool IsAdmin);

public sealed record LoginResponse(
    [property: JsonPropertyName("expires_at")] string? ExpiresAt,
    [property: JsonPropertyName("must_change_password"), JsonConverter(typeof(NumericBoolConverter))] bool MustChangePassword,
    [property: JsonPropertyName("requires_first_admin"), JsonConverter(typeof(NumericBoolConverter))] bool RequiresFirstAdmin,
    string? Message,
    string? Token,
    SessionUser? User);

public sealed record MeSession([property: JsonPropertyName("expires_at")] string? ExpiresAt);

public sealed record MeResponse(
    SessionUser? User,
    string? Role,
    [property: JsonPropertyName("is_admin"), JsonConverter(typeof(NumericBoolConverter))] bool IsAdmin,
    [property: JsonPropertyName("must_change_password"), JsonConverter(typeof(NumericBoolConverter))] bool MustChangePassword,
    string? Permissions,
    MeSession? Session);

public sealed record UserItem(
    int Id,
    string Username,
    [property: JsonPropertyName("role_id")] int? RoleId,
    string? Role,
    [property: JsonPropertyName("is_admin"), JsonConverter(typeof(NumericBoolConverter))] bool IsAdmin,
    [property: JsonPropertyName("is_enabled"), JsonConverter(typeof(NumericBoolConverter))] bool IsEnabled,
    string? Permissions,
    [property: JsonPropertyName("allowed_protocols")] string? AllowedProtocols,
    [property: JsonPropertyName("age_restriction")] int? AgeRestriction,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("must_change_password"), JsonConverter(typeof(NumericBoolConverter))] bool MustChangePassword);

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string? Role,
    string? Permissions,
    string? AllowedProtocols,
    int? AgeRestriction);

public sealed class UpdateUserRequest
{
    public string? Role { get; init; }

    public string? Permissions { get; init; }

    public bool? IsEnabled { get; init; }

    public string? NewPassword { get; init; }

    public string? AllowedProtocols { get; init; }

    public int? AgeRestriction { get; init; }

    public bool? ClearAgeRestriction { get; init; }
}

public sealed record ProtocolItem(
    string Id,
    string Label,
    string Description,
    [property: JsonPropertyName("sends_credentials_over_network"), JsonConverter(typeof(NumericBoolConverter))] bool SendsCredentialsOverNetwork,
    string? Warning);

public sealed record ProtocolCatalog(
    [property: JsonPropertyName("secure_default")] string? SecureDefault,
    List<ProtocolItem>? Protocols);

public sealed record UpdatedResult(bool Updated);

public sealed record DeletedResult(bool Deleted);

public sealed record SuccessResult(bool Success);

public sealed record RoleItem(int Id, string Name, string? Description, bool IsAdmin, int UserCount);
