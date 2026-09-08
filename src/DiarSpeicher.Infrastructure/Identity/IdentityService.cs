using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Identity;
using DiarSpeicher.Core.Security;
using DiarSpeicher.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private const int MinimumPasswordLength = 8;

    private readonly DiarSpeicherDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public IdentityService(DiarSpeicherDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public Task<bool> IsClaimedAsync(CancellationToken ct = default) =>
        _db.Users.AnyAsync(u => u.DeletedAt == null, ct);

    public async Task<IdentityResult<UserDto>> ClaimAsync(ClaimServerRequest request, CancellationToken ct = default)
    {
        if (await IsClaimedAsync(ct))
        {
            return IdentityResult<UserDto>.Fail(IdentityErrorCode.AlreadyClaimed, "The server has already been claimed.");
        }

        var validation = ValidateCredentials(request.Username, request.Password);
        if (validation != null)
        {
            return IdentityResult<UserDto>.Fail(IdentityErrorCode.InvalidRequest, validation);
        }

        var owner = new User
        {
            Username = request.Username,
            IsServerOwner = true,
            HashedPassword = _passwordHasher.Hash(request.Password)
        };

        _db.Users.Add(owner);
        await _db.SaveChangesAsync(ct);

        return IdentityResult<UserDto>.Ok(ToDto(owner));
    }

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default) =>
        await _db.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null)
            .OrderBy(u => u.Username)
            .Select(u => new UserDto(u.Id, u.Username, u.IsServerOwner, u.IsLocked, u.CreatedAt))
            .ToListAsync(ct);

    public async Task<IdentityResult<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var validation = ValidateCredentials(request.Username, request.Password);
        if (validation != null)
        {
            return IdentityResult<UserDto>.Fail(IdentityErrorCode.InvalidRequest, validation);
        }

        if (await _db.Users.AnyAsync(u => u.Username == request.Username && u.DeletedAt == null, ct))
        {
            return IdentityResult<UserDto>.Fail(IdentityErrorCode.UsernameTaken, "That username is already in use.");
        }

        var user = new User
        {
            Username = request.Username,
            IsServerOwner = request.IsServerOwner,
            HashedPassword = _passwordHasher.Hash(request.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return IdentityResult<UserDto>.Ok(ToDto(user));
    }

    public async Task<IdentityResult<UserDto>> UpdateUserAsync(string userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user == null)
        {
            return IdentityResult<UserDto>.Fail(IdentityErrorCode.UserNotFound, "User not found.");
        }

        if (request.Password != null)
        {
            if (request.Password.Length < MinimumPasswordLength)
            {
                return IdentityResult<UserDto>.Fail(
                    IdentityErrorCode.InvalidRequest,
                    $"The password must be at least {MinimumPasswordLength} characters long.");
            }

            user.HashedPassword = _passwordHasher.Hash(request.Password);
        }

        if (request.IsLocked.HasValue)
        {
            user.IsLocked = request.IsLocked.Value;
        }

        if (request.MaxSessionsAllowed.HasValue)
        {
            user.MaxSessionsAllowed = request.MaxSessionsAllowed.Value;
        }

        await _db.SaveChangesAsync(ct);

        return IdentityResult<UserDto>.Ok(ToDto(user));
    }

    /// <summary>
    /// Soft delete: reading sessions and progress reference the user, so the row stays and
    /// authentication rejects it through <see cref="User.DeletedAt"/>. Sessions and API keys
    /// are removed outright so no credential outlives the account.
    /// </summary>
    public async Task<IdentityResult<bool>> DeleteUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user == null)
        {
            return IdentityResult<bool>.Fail(IdentityErrorCode.UserNotFound, "User not found.");
        }

        user.DeletedAt = DateTimeOffset.UtcNow;

        await _db.ApiKeys.Where(k => k.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.SaveChangesAsync(ct);

        return IdentityResult<bool>.Ok(true);
    }

    /// <summary>
    /// Raw SQL for the same reason as the reading feeds: EF will not translate an ORDER BY
    /// over a DateTimeOffset on SQLite.
    /// </summary>
    public async Task<IReadOnlyList<ApiKeyDto>> GetApiKeysAsync(string userId, CancellationToken ct = default)
    {
        var keys = await _db.ApiKeys
            .FromSqlRaw(
                """
                SELECT * FROM "ApiKeys" WHERE "UserId" = @userId ORDER BY "CreatedAt" DESC
                """,
                new SqliteParameter("@userId", userId))
            .AsNoTracking()
            .ToListAsync(ct);

        return [.. keys.Select(k => new ApiKeyDto(k.Id, k.Name, k.CreatedAt, k.ExpiresAt, k.LastUsedAt))];
    }

    public async Task<IdentityResult<CreatedApiKeyDto>> CreateApiKeyAsync(string userId, CreateApiKeyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return IdentityResult<CreatedApiKeyDto>.Fail(IdentityErrorCode.InvalidRequest, "The API key name is required.");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null, ct))
        {
            return IdentityResult<CreatedApiKeyDto>.Fail(IdentityErrorCode.UserNotFound, "User not found.");
        }

        var plaintext = ApiKeyGenerator.GenerateKey();
        var apiKey = new ApiKey
        {
            UserId = userId,
            Name = request.Name,
            KeyHash = ApiKeyGenerator.HashKey(plaintext),
            ExpiresAt = request.ExpiresAt
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(ct);

        return IdentityResult<CreatedApiKeyDto>.Ok(
            new CreatedApiKeyDto(apiKey.Id, apiKey.Name, plaintext, apiKey.CreatedAt, apiKey.ExpiresAt));
    }

    public async Task<IdentityResult<bool>> RevokeApiKeyAsync(string userId, string apiKeyId, CancellationToken ct = default)
    {
        var removed = await _db.ApiKeys
            .Where(k => k.Id == apiKeyId && k.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return removed == 0
            ? IdentityResult<bool>.Fail(IdentityErrorCode.UserNotFound, "API key not found.")
            : IdentityResult<bool>.Ok(true);
    }

    private static string? ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "The username is required.";
        }

        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            return $"The password must be at least {MinimumPasswordLength} characters long.";
        }

        return null;
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Username, user.IsServerOwner, user.IsLocked, user.CreatedAt);
}
