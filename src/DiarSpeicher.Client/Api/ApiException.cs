namespace DiarSpeicher.Client.Api;

public sealed class ApiException(int status, string message, string? details = null) : Exception(message)
{
    public int Status { get; } = status;

    public string? Details { get; } = details;
}
