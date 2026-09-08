using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Api.GraphQL;

public class Query
{
    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Library> GetLibraries(
        DiarSpeicherDbContext db,
        AuthUserResolver auth) =>
        db.Libraries.ForUser(auth.Require());

    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Series> GetSeries(
        DiarSpeicherDbContext db,
        AuthUserResolver auth,
        string? libraryId = null)
    {
        var query = db.Series.ForUser(auth.Require());

        return libraryId == null ? query : query.Where(s => s.LibraryId == libraryId);
    }

    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Media> GetMedia(
        DiarSpeicherDbContext db,
        AuthUserResolver auth,
        string? seriesId = null)
    {
        var query = db.Media.ForUser(auth.Require());

        return seriesId == null ? query : query.Where(m => m.SeriesId == seriesId);
    }

    /// <summary>
    /// Books the caller is part-way through. Scoped to the caller's own sessions and to
    /// media they may see, so an excluded library never surfaces through reading history.
    /// </summary>
    [UsePaging]
    [UseFiltering]
    [UseSorting]
    public IQueryable<ReadingSession> GetReadingSessions(
        DiarSpeicherDbContext db,
        AuthUserResolver auth)
    {
        var user = auth.Require();
        var visibleMedia = db.Media.ForUser(user).Select(m => m.Id);

        return db.ReadingSessions
            .Where(rs => rs.UserId == user.Id && visibleMedia.Contains(rs.MediaId));
    }

    public ServerConfig GetServerConfig(IOptions<StorageOptions> storageOptions)
    {
        var upload = storageOptions.Value.Upload;

        return new ServerConfig
        {
            Version = "1.0.0",
            UploadEnabled = upload.EnableUpload,
            MaxFileUploadSize = upload.MaxFileUploadSize,
            AllowedExtensions = [.. upload.ResolveAllowedExtensions()]
        };
    }
}

public class ServerConfig
{
    public string Version { get; init; } = string.Empty;
    public bool UploadEnabled { get; init; }
    public long MaxFileUploadSize { get; init; }
    public List<string> AllowedExtensions { get; init; } = [];
}

public static class GraphQLConstants
{
    public const string AuthUserKey = "AuthUser";
}
