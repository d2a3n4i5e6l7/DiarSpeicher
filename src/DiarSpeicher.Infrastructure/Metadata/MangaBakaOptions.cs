namespace DiarSpeicher.Infrastructure.Metadata;

public class MangaBakaOptions
{
    public const string SectionName = "MangaBaka";

    /// <summary>
    /// Never versioned: gigabytes under a third-party license, populated by the user from the
    /// UI. Empty by default on purpose: it is filled with &lt;Storage
    /// RootPath&gt;/manga_database, the mounted volume with permissions. The process working
    /// directory does not work: in the container it is /home/nonroot, neither writable nor
    /// persistent.
    /// </summary>
    public string? DatabasePath { get; set; }

    public string DownloadUrl { get; set; } = "https://mangabaka.org/data/database/series.sqlite.zst";

    /// <summary>
    /// Upper bound for a download or upload. The dump is around 400 MB compressed; the margin
    /// covers its growth without leaving the door wide open.
    /// </summary>
    public long MaxArchiveBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// A whitelist rather than any filter because the proxy receives the URL from the client:
    /// without bounding it, it would be an open proxy for asking the server to talk to any
    /// address on its internal network.
    /// </summary>
    public List<string> CoverHosts { get; set; } = ["mangabaka.dev", "cdn.mangabaka.dev"];

    public bool IsAllowedCover(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && CoverHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    public string ResolveDatabasePath() => Path.GetFullPath(
        string.IsNullOrWhiteSpace(DatabasePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "manga_database")
            : DatabasePath);

    public string ResolveSqlitePath() => Path.Combine(ResolveDatabasePath(), "series.sqlite");
}
