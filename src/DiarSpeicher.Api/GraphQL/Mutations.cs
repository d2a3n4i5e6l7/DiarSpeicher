using DiarSpeicher.Core.Domain.Entities;
using DiarSpeicher.Core.Domain.Enums;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Background;
using DiarSpeicher.Infrastructure.Data;
using DiarSpeicher.Infrastructure.Data.Extensions;
using DiarSpeicher.Infrastructure.Filesystem;
using Microsoft.Extensions.Options;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;

namespace DiarSpeicher.Api.GraphQL;

public class Mutation
{
    public async Task<Library> CreateLibrary(
        DiarSpeicherDbContext db,
        AuthUserResolver auth,
        IOptions<LibraryRootsOptions> libraryRoots,
        CreateLibraryInput input,
        CancellationToken ct)
    {
        // Misma jaula que en REST: si esta puerta no la comprueba, la lista blanca de
        // carpetas no sirve de nada porque se entra por la de al lado.
        if (!libraryRoots.Value.TryResolve(input.Path, out var fullPath))
        {
            throw new GraphQLException($"The path '{input.Path}' is outside the allowed library roots.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new GraphQLException($"The path '{input.Path}' does not exist.");
        }

        var library = new Library
        {
            Name = input.Name,
            Path = fullPath,
            Description = input.Description,
            Config = new LibraryConfig()
        };

        db.Libraries.Add(library);
        await db.SaveChangesAsync(ct);

        return library;
    }

    public static async Task<Library> EditLibrary(
        DiarSpeicherDbContext db,
        AuthUserResolver auth,
        EditLibraryInput input,
        CancellationToken ct)
    {
        var user = RequireServerOwner(auth);

        var library = await db.Libraries.ForUser(user).FirstOrDefaultAsync(l => l.Id == input.Id, ct)
            ?? throw new GraphQLException($"Library '{input.Id}' not found.");

        if (input.Name != null) library.Name = input.Name;
        if (input.Description != null) library.Description = input.Description;
        if (input.Emoji != null) library.Emoji = input.Emoji;

        library.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return library;
    }

    public static async Task<bool> DeleteLibrary(
        DiarSpeicherDbContext db,
        AuthUserResolver auth,
        string id,
        CancellationToken ct)
    {
        var user = RequireServerOwner(auth);

        var library = await db.Libraries.ForUser(user).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (library == null)
        {
            return false;
        }

        db.Libraries.Remove(library);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Channels into IScannerQueue rather than scanning inline, so the mutation returns
    /// immediately and the work runs on the same background worker as every other scan.
    /// </summary>
    public async Task<ScanJob> ScanLibrary(
        DiarSpeicherDbContext db,
        IScannerQueue queue,
        AuthUserResolver auth,
        string id,
        CancellationToken ct)
    {
        var user = auth.Require();
        if (!user.HasPermission(Permissions.ScanLibrary))
        {
            throw new GraphQLException("This account is not allowed to scan libraries.");
        }

        var exists = await db.Libraries.ForUser(user).AnyAsync(l => l.Id == id, ct);
        if (!exists)
        {
            throw new GraphQLException($"Library '{id}' not found.");
        }

        await queue.QueueScanAsync(new ScanRequest(id), ct);

        return new ScanJob { JobId = id, LibraryId = id, Queued = true };
    }

    public async Task<ReadingSession> UpdateReadingProgress(
        DiarSpeicherDbContext db,
        AuthUserResolver auth,
        UpdateReadingProgressInput input,
        CancellationToken ct)
    {
        var user = auth.Require();
        var media = await db.Media.ForUser(user).FirstOrDefaultAsync(m => m.Id == input.MediaId, ct)
            ?? throw new GraphQLException($"Media '{input.MediaId}' not found.");

        var session = await db.ReadingSessions
            .Where(rs => rs.UserId == user.Id && rs.MediaId == media.Id)
            .OrderByDescending(rs => rs.Id)
            .FirstOrDefaultAsync(ct);

        if (session == null)
        {
            session = new ReadingSession
            {
                UserId = user.Id,
                MediaId = media.Id
            };

            db.ReadingSessions.Add(session);
        }

        if (input.Page.HasValue) session.EndPage = input.Page;
        if (input.Percentage.HasValue) session.EndPercentage = input.Percentage;
        if (input.IsCompleted == true) session.Status = ReadingStatus.Finished;

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return session;
    }

    private static AuthUser RequireServerOwner(AuthUserResolver auth)
    {
        var user = auth.Require();
        if (!user.IsServerOwner)
        {
            throw new GraphQLException("Only the server owner may perform this operation.");
        }

        return user;
    }
}

public record CreateLibraryInput(string Name, string Path, string? Description);

public record EditLibraryInput(string Id, string? Name, string? Description, string? Emoji);

public record UpdateReadingProgressInput(string MediaId, int? Page, decimal? Percentage, bool? IsCompleted);

public class ScanJob
{
    public string JobId { get; init; } = string.Empty;
    public string LibraryId { get; init; } = string.Empty;
    public bool Queued { get; init; }
}

