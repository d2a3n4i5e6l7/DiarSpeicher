using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Auth;
using DiarSpeicher.Client.Components.Users;
using DiarSpeicher.Core.Domain.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DiarSpeicher.Client.Pages;

public sealed partial class Users : IDisposable
{
    private const int SuccessVisibleMs = 6000;
    private const string AdminUsername = "admin";

    private static readonly DialogOptions SmallDialog = new() { MaxWidth = MaxWidth.Small, FullWidth = true };
    private static readonly DialogOptions LargeDialog = new() { MaxWidth = MaxWidth.Large, FullWidth = true };
    private static readonly DialogOptions ExtraSmallDialog = new() { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };

    private static readonly IReadOnlyList<ProtocolItem> FallbackProtocols =
    [
        new(PermissionService.ProtocolApiKey, "Clave de API (KOReader, Kobo, OPDS)",
            "Clave revocable por dispositivo. No expone la contraseña de la cuenta.", SendsCredentialsOverNetwork: false, Warning: null),
        new(PermissionService.ProtocolBasic, "OPDS con usuario y contraseña (HTTP Basic)",
            "Necesario para lectores como Panels, Chunky, Moon+ o Aldiko.", SendsCredentialsOverNetwork: true, Warning: null),
    ];

    private List<UserItem> _users = [];
    private IReadOnlyList<RoleItem> _roles = [];
    private IReadOnlyList<ProtocolItem> _protocols = FallbackProtocols;
    private bool _loading = true;
    private bool _saving;
    private string? _error;
    private string? _success;
    private CancellationTokenSource? _successTimer;

    private bool _createOpen;
    private UserFormState _createForm = UserFormState.Empty();

    private bool _editOpen;
    private UserItem? _editingUser;
    private UserFormState _editForm = UserFormState.Empty();
    private int _editTab;
    private List<EpubDeviceProfile> _epubProfiles = [];
    private bool _loadingProfiles;

    private bool _passwordOpen;
    private UserItem? _passwordUser;
    private string _newPassword = string.Empty;

    private bool _deleteOpen;
    private UserItem? _deleteUser;

    [Inject] private UsersApi UsersApi { get; set; } = null!;

    [Inject] private RolesApi RolesApi { get; set; } = null!;

    [Inject] private EpubProfilesApi EpubProfilesApi { get; set; } = null!;

    public void Dispose() => CancelSuccessTimer();

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
        try
        {
            var catalog = await UsersApi.ProtocolsAsync();
            if (catalog?.Protocols is { Count: > 0 } protocols)
            {
                _protocols = protocols;
            }
        }
        catch (ApiException)
        {
        }
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var users = UsersApi.ListAsync();
            var roles = RolesApi.ListAsync();
            await Task.WhenAll(users, roles);
            _users = await users ?? [];
            _roles = await roles;
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private IEnumerable<string> RoleOptions(string current)
    {
        var names = _roles.Select(role => role.Name).ToList();
        if (current.Length > 0 && !names.Contains(current))
        {
            names.Add(current);
        }

        return names;
    }

    private static string AgeLabel(UserItem user) => user.AgeRestriction is { } age ? $"+{age}" : "SIN LÍMITE";

    private static bool CanDelete(UserItem user) => user.Username != AdminUsername;

    private void OpenCreate()
    {
        _createForm = UserFormState.Empty();
        _createOpen = true;
    }

    private async Task OpenEditAsync(UserItem user)
    {
        _editingUser = user;
        _editForm = UserFormState.FromUser(user);
        _editTab = 0;
        _editOpen = true;
        _loadingProfiles = true;
        try
        {
            var loaded = await EpubProfilesApi.GetProfilesAsync(user.Id);
            _epubProfiles = loaded is { Count: > 0 } ? loaded : [EpubProfileDefaults.Create()];
        }
        catch (ApiException)
        {
            _epubProfiles = [EpubProfileDefaults.Create()];
        }
        finally
        {
            _loadingProfiles = false;
        }
    }

    private void OpenPassword(UserItem user)
    {
        _passwordUser = user;
        _newPassword = string.Empty;
        _passwordOpen = true;
    }

    private void OpenDelete(UserItem user)
    {
        _deleteUser = user;
        _deleteOpen = true;
    }

    private void CloseCreate() => _createOpen = false;

    private void CloseEdit() => _editOpen = false;

    private void ClosePassword() => _passwordOpen = false;

    private void CloseDelete() => _deleteOpen = false;

    private void ClearError() => _error = null;

    private void ClearSuccess() => _success = null;

    private async Task CreateUserAsync()
    {
        var username = _createForm.Username.Trim();
        if (username.Length == 0 || _createForm.Password.Length == 0)
        {
            _error = "El usuario y la contraseña son obligatorios.";
            return;
        }

        await SaveAsync(async () =>
        {
            await UsersApi.CreateAsync(new CreateUserRequest(
                username,
                _createForm.Password,
                _createForm.Role.Length > 0 ? _createForm.Role : UserFormState.DefaultRole,
                _createForm.SerializePermissions(),
                _createForm.SerializeProtocols(),
                _createForm.ParseAge()));
            ShowSuccess($"Usuario \"{_createForm.Username}\" creado con éxito.");
            _createOpen = false;
            _createForm = UserFormState.Empty();
            await LoadDataAsync();
        });
    }

    private async Task UpdateUserAsync()
    {
        if (_editingUser is not { } user)
        {
            return;
        }

        await SaveAsync(async () =>
        {
            var update = UsersApi.UpdateAsync(user.Id, new UpdateUserRequest
            {
                Role = _editForm.Role.Length > 0 ? _editForm.Role : null,
                Permissions = _editForm.SerializePermissions(),
                IsEnabled = _editForm.IsEnabled,
                AllowedProtocols = _editForm.SerializeProtocols(),
                AgeRestriction = _editForm.ParseAge(),
                ClearAgeRestriction = _editForm.HasAge ? null : true,
            });
            var profiles = EpubProfilesApi.SaveProfilesAsync(user.Id, _epubProfiles);
            await Task.WhenAll(update, profiles);
            var repaginated = (await profiles)?.AffectedBooks ?? 0;
            ShowSuccess(repaginated > 0
                ? $"Usuario \"{user.Username}\" actualizado. La nueva maquetación repagina {repaginated} libro(s) sin terminar: su progreso se reajustará al reabrirlos y quedará cerca, no exacto."
                : $"Usuario \"{user.Username}\" actualizado.");
            _editOpen = false;
            await LoadDataAsync();
        });
    }

    private async Task ChangePasswordAsync()
    {
        if (_passwordUser is not { } user || _newPassword.Length == 0)
        {
            return;
        }

        await SaveAsync(async () =>
        {
            await UsersApi.ChangePasswordAsync(user.Id, _newPassword);
            ShowSuccess($"Contraseña de \"{user.Username}\" cambiada con éxito.");
            _passwordOpen = false;
            _newPassword = string.Empty;
        });
    }

    private async Task DeleteUserAsync()
    {
        if (_deleteUser is not { } user)
        {
            return;
        }

        await SaveAsync(async () =>
        {
            await UsersApi.DeleteAsync(user.Id);
            ShowSuccess($"Usuario \"{user.Username}\" eliminado.");
            _deleteOpen = false;
            _deleteUser = null;
            await LoadDataAsync();
        });
    }

    private async Task SaveAsync(Func<Task> operation)
    {
        _saving = true;
        _error = null;
        try
        {
            await operation();
        }
        catch (ApiException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    private void ShowSuccess(string message)
    {
        CancelSuccessTimer();
        _success = message;
        _successTimer = new CancellationTokenSource();
        _ = HideSuccessLaterAsync(_successTimer.Token);
    }

    private async Task HideSuccessLaterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SuccessVisibleMs, cancellationToken);
            _success = null;
            await InvokeAsync(StateHasChanged);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void CancelSuccessTimer()
    {
        _successTimer?.Cancel();
        _successTimer?.Dispose();
        _successTimer = null;
    }
}
