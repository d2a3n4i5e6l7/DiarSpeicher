namespace DiarSpeicher.Infrastructure.Data.Extensions;

/// <summary>
/// Dapper.AOT only binds typed members, so the catalog queries whose SQL is assembled at
/// runtime share one args type. Every member is sent as a parameter; Microsoft.Data.Sqlite
/// ignores the ones a statement does not reference.
/// </summary>
public sealed class CatalogQueryArgs
{
    public string? ExcludedLibraries { get; set; }
    public int? MaxAge { get; set; }
    public int RestrictOnUnset { get; set; }
    public int NewestFirst { get; set; }
    public string? Id { get; set; }
    public string? LibraryId { get; set; }
    public string? SeriesId { get; set; }
    public string? MediaId { get; set; }
    public string? UserId { get; set; }
    public string? MediaIds { get; set; }
    public string? SeriesKeys { get; set; }
    public string? LibraryKeys { get; set; }
    public string? Status { get; set; }
    public string? MissingStatus { get; set; }
    public string? Root { get; set; }
    public string? Prefix { get; set; }
    public int? PrefixLen { get; set; }
    public string? SearchPattern { get; set; }
    public string? KoreaderHash { get; set; }
    public string? Document { get; set; }
    public int? PageSize { get; set; }
    public int? Offset { get; set; }
    public int? Take { get; set; }
    public int? Skip { get; set; }
    public int? LimitPlusOne { get; set; }
}
