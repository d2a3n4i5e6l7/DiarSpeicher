using DiarSpeicher.Core.Domain.Models;
using BackendPermissions = DiarSpeicher.Core.Domain.Models.Permissions;

namespace DiarSpeicher.Client.Auth;

public sealed record PermissionItem(string Id, string Label, string Description);

public sealed record PermissionGroup(string Title, IReadOnlyList<PermissionItem> Permissions);

public static class PermissionService
{
    public const string AccessApiKeys = "AccessApiKeys";

    public const string ProtocolApiKey = "api_key";
    public const string ProtocolBasic = "basic_password";
    public const string DefaultPermissions = $"{AccessApiKeys},{BackendPermissions.AccessKoreaderSync},{BackendPermissions.AccessKoboSync}";

    public static IReadOnlyList<PermissionGroup> Groups { get; } =
    [
        new("Contenido",
        [
            new(BackendPermissions.FileUpload, "Subir ficheros", "Enviar libros a una biblioteca, por subida directa o por TUS."),
            new(BackendPermissions.CreateFolder, "Crear carpetas",
                "Subir a una subcarpeta que aun no existe. Subir a una que ya existe no lo necesita."),
        ]),
        new("Bibliotecas",
        [
            new(BackendPermissions.ManageLibrary, "Crear bibliotecas",
                "Dar de alta bibliotecas nuevas. Editarlas y borrarlas sigue siendo exclusivo del propietario del servidor."),
            new(BackendPermissions.ScanLibrary, "Lanzar escaneos", "Encolar el escaneo de una biblioteca."),
        ]),
        new("Clientes de lectura",
        [
            new(AccessApiKeys, "Claves de API",
                "Crear y revocar sus propias claves de dispositivo. Lo comprueba el Gateway, no DiarSpeicher."),
            new(BackendPermissions.AccessKoreaderSync, "Sincronizacion KOReader", "Usar el endpoint /koreader para guardar el progreso."),
            new(BackendPermissions.AccessKoboSync, "Sincronizacion Kobo", "Usar el endpoint /kobo para sincronizar con el dispositivo."),
        ]),
    ];

    public static List<string> ParseCsv(string? raw) =>
        string.IsNullOrEmpty(raw)
            ? []
            : [.. raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    public static bool HasPermission(string? permissions, string id)
    {
        var user = new AuthUser();
        user.Permissions.UnionWith(ParseCsv(permissions));
        return user.HasPermission(id);
    }

    public static bool HasProtocol(string? allowedProtocols, string id) =>
        ParseCsv(allowedProtocols).Exists(entry => string.Equals(entry, id, StringComparison.OrdinalIgnoreCase));
}
