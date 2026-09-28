using Dapper;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Catalog;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DiarSpeicher.Tests;

/// <summary>
/// Acceptance criteria of block B2.4: uploads disabled reject the request, a file above the
/// per-file limit is rejected without leaving anything on disk, and an extension outside the
/// whitelist is rejected.
/// </summary>
public sealed class UploadPolicyTests : IDisposable
{
    private readonly TestSqliteDatabase _db;
    private readonly string _libraryDir;

    public UploadPolicyTests()
    {
        _db = new TestSqliteDatabase();
        _libraryDir = Directory.CreateTempSubdirectory("diar-upload-tests-").FullName;
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_libraryDir)) Directory.Delete(_libraryDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task UploadDisabled_IsRejected()
    {
        var (service, libraryId, owner) = await ArrangeAsync(options => options.EnableUpload = false);

        var result = await service.UploadToLibraryAsync(owner, libraryId, null, [File("libro.cbz", 10)]);

        Assert.Equal(UploadOutcome.UploadDisabled, result.Outcome);
    }

    [Fact]
    public async Task FileOverTheLimit_IsRejected_AndNothingIsLeftOnDisk()
    {
        var (service, libraryId, owner) = await ArrangeAsync(options => options.MaxFileUploadSize = 1_000);

        var result = await service.UploadToLibraryAsync(owner, libraryId, null, [File("grande.cbz", 5_000)]);

        Assert.Equal(UploadOutcome.FileTooLarge, result.Outcome);
        Assert.False(System.IO.File.Exists(Path.Combine(_libraryDir, "grande.cbz")));
    }

    [Fact]
    public async Task FileWithinTheLimit_IsAccepted()
    {
        var (service, libraryId, owner) = await ArrangeAsync(options => options.MaxFileUploadSize = 10_000);

        var result = await service.UploadToLibraryAsync(owner, libraryId, null, [File("pequeno.cbz", 500)]);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Equal(1, result.Response!.UploadedCount);

        var uploaded = Assert.Single(result.Response.Files);
        Assert.Equal("pequeno.cbz", uploaded.Name);
        Assert.Equal(500, uploaded.Size);
        Assert.Equal(Path.Combine(_libraryDir, "pequeno.cbz"), uploaded.Path);
    }

    [Fact]
    public async Task ExtensionOutsideTheWhitelist_IsRejected()
    {
        var (service, libraryId, owner) = await ArrangeAsync(options => options.AllowedExtensions = [".cbz"]);

        var result = await service.UploadToLibraryAsync(owner, libraryId, null, [File("notas.txt", 10)]);

        Assert.Equal(UploadOutcome.ExtensionNotAllowed, result.Outcome);
        Assert.False(System.IO.File.Exists(Path.Combine(_libraryDir, "notas.txt")));
    }

    [Fact]
    public void AnEmptyExtensionListFallsBackToTheDefaults()
    {
        var options = new DiarSpeicher.Infrastructure.Storage.UploadOptions();

        Assert.True(options.IsExtensionAllowed("comic.cbz"));
        Assert.True(options.IsExtensionAllowed("libro.EPUB"));
        Assert.False(options.IsExtensionAllowed("notas.txt"));
    }

    private static DiarSpeicherUploadFileInput File(string name, int bytes) =>
        new() { FileName = name, Content = new MemoryStream(new byte[bytes]) };

    private async Task<(DiarSpeicherService Service, string LibraryId, AuthUser Owner)> ArrangeAsync(
        Action<DiarSpeicher.Infrastructure.Storage.UploadOptions> configure)
    {
        var conn = _db.Connection;

        var configId = await conn.QuerySingleAsync<long>("""
            INSERT INTO "LibraryConfigs" DEFAULT VALUES RETURNING "Id";
            """);

        var libraryId = Guid.NewGuid().ToString();
        await conn.ExecuteAsync("""
            INSERT INTO "Libraries" ("Id", "Name", "Path", "Status", "ConfigId", "CreatedAt")
            VALUES (@Id, 'Comics', @Path, 'Ready', @ConfigId, @Now);
            """, new
        {
            Id = libraryId,
            Path = _libraryDir,
            ConfigId = configId,
            Now = DateTimeOffset.UtcNow
        });

        var composite = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });
        var service = new DiarSpeicherService(
            _db.Factory,
            composite,
            new ScannerQueue(),
            NullLogger<DiarSpeicherService>.Instance,
            TestStorageOptions.With(configure),
            TestReadingProgress.For(_db.Factory));

        return (service, libraryId, new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true });
    }
}
