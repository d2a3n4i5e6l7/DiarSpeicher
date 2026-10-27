namespace DiarSpeicher.Core.Domain.Models;

/// <summary>
/// The Gateway stores <c>reader_user.permissions</c> as free CSV and does not validate the names:
/// the only one it interprets itself is <c>AccessApiKeys</c>, to decide whether a user manages
/// their own keys. The rest of the vocabulary is fixed by this file together with
/// <c>PermissionService.Groups</c> in the client (<c>src/DiarSpeicher.Client/Auth/PermissionService.cs</c>), and both have to
/// say the same thing: a name that exists on only one side is a switch that turns nothing off.
///
/// Only permissions some endpoint actually checks are declared here; a constant without its
/// check is another such switch.
/// </summary>
public static class Permissions
{
    public const string FileUpload = "FileUpload";

    public const string CreateFolder = "CreateFolder";

    public const string ManageLibrary = "ManageLibrary";

    public const string ScanLibrary = "ScanLibrary";

    public const string AccessKoreaderSync = "AccessKoreaderSync";

    public const string AccessKoboSync = "AccessKoboSync";
}
