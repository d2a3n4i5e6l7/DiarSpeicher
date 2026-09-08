using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Infrastructure.Komga;

public interface IKomgaService
{
    Task<List<KomgaLibraryDto>> GetLibrariesAsync(AuthUser user, CancellationToken ct = default);
    Task<KomgaLibraryDto?> GetLibraryByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<KomgaPageResponse<KomgaSeriesDto>> GetSeriesAsync(AuthUser user, string? libraryId, string? search, int page, int size, CancellationToken ct = default);
    Task<KomgaSeriesDto?> GetSeriesByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<KomgaPageResponse<KomgaBookDto>> GetBooksAsync(AuthUser user, string? search, int page, int size, CancellationToken ct = default);
    Task<KomgaPageResponse<KomgaBookDto>> GetLatestBooksAsync(AuthUser user, int page, int size, CancellationToken ct = default);
    Task<KomgaPageResponse<KomgaBookDto>> GetBooksInSeriesAsync(AuthUser user, string seriesId, int page, int size, CancellationToken ct = default);
    Task<KomgaBookDto?> GetBookByIdAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<List<KomgaBookPageDto>> GetBookPagesAsync(AuthUser user, string id, CancellationToken ct = default);
    Task<bool> UpdateReadProgressAsync(AuthUser user, string bookId, int page, bool completed, CancellationToken ct = default);
    Task<bool> DeleteReadProgressAsync(AuthUser user, string bookId, CancellationToken ct = default);
}
