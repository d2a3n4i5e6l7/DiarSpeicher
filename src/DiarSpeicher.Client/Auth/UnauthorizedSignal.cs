namespace DiarSpeicher.Client.Auth;

public sealed class UnauthorizedSignal
{
    public event Action? Raised;

    public void Raise() => Raised?.Invoke();
}
