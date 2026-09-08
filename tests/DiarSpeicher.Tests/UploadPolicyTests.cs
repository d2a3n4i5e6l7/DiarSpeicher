using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.StumpV2;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Filesystem.Processors;
using DiarSpeicher.Infrastructure.StumpV2;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiarSpeicher.Tests;

/// <summary>
/// Acceptance criteria of block B2.4: uploads disabled reject the request, a file above the
/// per-file limit is rejected without leaving anything on disk, and an extension outside the
/// whitelist is rejected.
/// </summary>
public sealed class UploadPolicyTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DiarSpeicherDbContext> _options;
    private readonly string _libraryDir;

    public UploadPolicyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DiarSpeicherDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new DiarSpeicherDbContext(_options);
        context.Database.EnsureCreated();

        _libraryDir = Directory.CreateTempSubdirectory("diar-upload-tests-").FullName;
    }

    public void Dispose()
    {
        _connection.Dispose();
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

    private static StumpUploadFileInput File(string name, int bytes) =>
        new() { FileName = name, Content = new MemoryStream(new byte[bytes]) };

    private async Task<(StumpV2Service Service, string LibraryId, AuthUser Owner)> ArrangeAsync(
        Action<DiarSpeicher.Infrastructure.Storage.UploadOptions> configure)
    {
        var db = new DiarSpeicherDbContext(_options);

        var library = new Library
        {
            Name = "Comics",
            Path = _libraryDir,
            Status = FileStatus.Ready,
            Config = new LibraryConfig()
        };

        db.Libraries.Add(library);
        await db.SaveChangesAsync();

        var composite = new CompositeBookProcessor(new IBookProcessor[] { new ZipBookProcessor() });
        var service = new StumpV2Service(
            db,
            composite,
            new ScannerQueue(),
            NullLogger<StumpV2Service>.Instance,
            TestStorageOptions.With(configure));

        return (service, library.Id, new AuthUser { Id = "admin", Username = "admin", IsServerOwner = true });
    }
}
