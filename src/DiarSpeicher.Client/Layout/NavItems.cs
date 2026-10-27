using MudBlazor;

namespace DiarSpeicher.Client.Layout;

public sealed record NavItem(string Path, string Label, string Icon, bool Exact = false)
{
    public bool IsActive(string relativePath) =>
        Exact ? relativePath.Length == 0 : relativePath.StartsWith(Path, StringComparison.OrdinalIgnoreCase);
}

public sealed record NavGroup(string Title, IReadOnlyList<NavItem> Items);

public static class NavItems
{
    public const string DefaultTitle = "Archivo DiarSpeicher";

    public static IReadOnlyList<NavGroup> Groups { get; } =
    [
        new("// ARCHIVO",
        [
            new(string.Empty, "Inicio", Icons.Material.Filled.Home, Exact: true),
            new("series", "Series", Icons.Material.Filled.CollectionsBookmark),
        ]),
        new("// SECTOR CONTROL",
        [
            new("libraries", "Bibliotecas", Icons.Material.Filled.LibraryBooks),
            new("users", "Usuarios del Reino", Icons.Material.Filled.People),
            new("roles", "Roles y Permisos", Icons.Material.Filled.AdminPanelSettings),
            new("metadata", "Metadata Externa", Icons.Material.Filled.Storage),
            new("trash", "Restaurar", Icons.Material.Filled.Restore),
        ]),
    ];

    public static string TitleFor(string relativePath) =>
        Groups.SelectMany(group => group.Items)
            .Where(item => item.IsActive(relativePath))
            .OrderByDescending(item => item.Path.Length)
            .Select(item => item.Label)
            .FirstOrDefault() ?? DefaultTitle;
}
