using DiarSpeicher.Core.Domain.Identity;

namespace DiarSpeicher.Infrastructure.Identity;

public interface IIdentityService
{
    Task<bool> IsClaimedAsync(CancellationToken ct = default);

    Task<IdentityResult<UserDto>> ClaimAsync(ClaimServerRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default);

    Task<IdentityResult<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<IdentityResult<UserDto>> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken ct = default);

    Task<IdentityResult<bool>> DeleteUserAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<ApiKeyDto>> GetApiKeysAsync(string userId, CancellationToken ct = default);

    Task<IdentityResult<CreatedApiKeyDto>> CreateApiKeyAsync(string userId, CreateApiKeyRequest request, CancellationToken ct = default);

    Task<IdentityResult<bool>> RevokeApiKeyAsync(string userId, string apiKeyId, CancellationToken ct = default);
}
