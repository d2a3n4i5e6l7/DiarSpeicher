namespace DiarSpeicher.Core.Domain.Models;

/// <summary>
/// Vocabulario de permisos que llega en <c>X-Auth-Perms</c>.
///
/// El Gateway guarda <c>reader_user.permissions</c> como un CSV libre y no valida los
/// nombres: el único que interpreta por su cuenta es <c>AccessApiKeys</c>, para decidir si
/// un usuario administra sus propias claves. El resto del vocabulario lo fija este fichero
/// junto con <c>PERMISSION_GROUPS</c> del panel (<c>frontEnd/src/api/endpoints.ts</c>), y
/// los dos tienen que decir lo mismo: un nombre que solo exista en uno de los dos lados es
/// un interruptor que no apaga nada.
///
/// Aquí solo se declaran los permisos que algún endpoint comprueba de verdad. Añadir una
/// constante sin su comprobación reproduce el problema que esto viene a cerrar.
/// </summary>
public static class Permissions
{
    /// <summary>Enviar ficheros a una biblioteca, por multipart o por TUS.</summary>
    public const string FileUpload = "FileUpload";

    /// <summary>Subir a una subcarpeta que todavía no existe, creándola.</summary>
    public const string CreateFolder = "CreateFolder";

    /// <summary>Crear bibliotecas nuevas.</summary>
    public const string ManageLibrary = "ManageLibrary";

    /// <summary>Encolar el escaneo de una biblioteca.</summary>
    public const string ScanLibrary = "ScanLibrary";

    /// <summary>Sincronizar el progreso de lectura desde KOReader.</summary>
    public const string AccessKoreaderSync = "AccessKoreaderSync";

    /// <summary>Sincronizar la biblioteca con un Kobo.</summary>
    public const string AccessKoboSync = "AccessKoboSync";
}
