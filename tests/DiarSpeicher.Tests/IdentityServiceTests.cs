using DiarSpeicher.Core.Domain.Identity;
using DiarSpeicher.Core.Security;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Tests;

/// <summary>
/// Acceptance criteria of block B1.3: the owner can be created through the API, claiming
/// twice conflicts, and API keys are issued showing the plaintext only once.
/// </summary>
public sealed class IdentityServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly IPasswordHasher _passwordHasher = new Pbkdf2PasswordHasher();

    public IdentityServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private IIdentityService NewService() =>
        new IdentityService(new DiarSpeicherDbContext(_options), _passwordHasher);

    [Fact]
    public async Task Claim_CreatesTheOwner()
    {
        var result = await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsServerOwner);
        Assert.True(await NewService().IsClaimedAsync());
    }

    [Fact]
    public async Task Claim_StoresTheHashAndNotThePassword()
    {
        await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"));

        await using var db = new DiarSpeicherDbContext(_options);
        var stored = await db.Users.SingleAsync();

        Assert.NotNull(stored.HashedPassword);
        Assert.DoesNotContain("SuperSecret123", stored.HashedPassword!, StringComparison.Ordinal);
        Assert.True(_passwordHasher.Verify("SuperSecret123", stored.HashedPassword));
    }

    [Fact]
    public async Task ClaimingTwice_Conflicts()
    {
        await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"));

        var second = await NewService().ClaimAsync(new ClaimServerRequest("otro", "SuperSecret123"));

        Assert.False(second.Succeeded);
        Assert.Equal(IdentityErrorCode.AlreadyClaimed, second.Error);
    }

    [Fact]
    public async Task Claim_RejectsAShortPassword()
    {
        var result = await NewService().ClaimAsync(new ClaimServerRequest("diar", "corta"));

        Assert.False(result.Succeeded);
        Assert.Equal(IdentityErrorCode.InvalidRequest, result.Error);
    }

    [Fact]
    public async Task CreateUser_RejectsADuplicateUsername()
    {
        await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"));

        var duplicate = await NewService().CreateUserAsync(new CreateUserRequest("diar", "OtraClave123", false));

        Assert.False(duplicate.Succeeded);
        Assert.Equal(IdentityErrorCode.UsernameTaken, duplicate.Error);
    }

    [Fact]
    public async Task ApiKey_IsStoredHashedAndTheKeyIsReturnedOnce()
    {
        var owner = (await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"))).Value!;

        var created = await NewService().CreateApiKeyAsync(owner.Id, new CreateApiKeyRequest("kobo", null));

        Assert.True(created.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(created.Value!.Key));

        await using var db = new DiarSpeicherDbContext(_options);
        var stored = await db.ApiKeys.SingleAsync();

        Assert.NotEqual(created.Value.Key, stored.KeyHash);
        Assert.Equal(ApiKeyGenerator.HashKey(created.Value.Key), stored.KeyHash);

        var listed = await NewService().GetApiKeysAsync(owner.Id);
        Assert.Single(listed);
    }

    [Fact]
    public async Task RevokingAnApiKey_RemovesIt()
    {
        var owner = (await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"))).Value!;
        var created = await NewService().CreateApiKeyAsync(owner.Id, new CreateApiKeyRequest("kobo", null));

        var revoked = await NewService().RevokeApiKeyAsync(owner.Id, created.Value!.Id);

        Assert.True(revoked.Succeeded);
        Assert.Empty(await NewService().GetApiKeysAsync(owner.Id));
    }

    [Fact]
    public async Task DeletingAUser_IsSoftAndRemovesTheirCredentials()
    {
        var owner = (await NewService().ClaimAsync(new ClaimServerRequest("diar", "SuperSecret123"))).Value!;
        var reader = (await NewService().CreateUserAsync(new CreateUserRequest("lector", "OtraClave123", false))).Value!;
        await NewService().CreateApiKeyAsync(reader.Id, new CreateApiKeyRequest("kobo", null));

        var deleted = await NewService().DeleteUserAsync(reader.Id);

        Assert.True(deleted.Succeeded);

        await using var db = new DiarSpeicherDbContext(_options);
        var stored = await db.Users.SingleAsync(u => u.Id == reader.Id);

        Assert.NotNull(stored.DeletedAt);
        Assert.Empty(await db.ApiKeys.Where(k => k.UserId == reader.Id).ToListAsync());
        Assert.Single(await NewService().GetUsersAsync());
        Assert.Equal(owner.Id, (await NewService().GetUsersAsync())[0].Id);
    }
}
