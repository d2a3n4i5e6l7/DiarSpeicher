using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DiarSpeicher.Client.Api;

public sealed class ApiClient(HttpClient http, string basePath)
{
    private const string ConnectionFailed = "No se pudo conectar con el servidor";
    private const string InvalidResponse = "Respuesta del servidor no válida";

    public string BasePath => basePath;

    public Task<TResult?> GetAsync<TResult>(string path, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, path, null, result, cancellationToken);

    public Task<TResult?> PostAsync<TResult>(string path, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, null, result, cancellationToken);

    public Task<TResult?> PostAsync<TBody, TResult>(
        string path, TBody body, JsonTypeInfo<TBody> bodyInfo, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, path, JsonContent.Create(body, bodyInfo), result, cancellationToken);

    public Task<TResult?> PutAsync<TResult>(string path, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, path, null, result, cancellationToken);

    public Task<TResult?> PutAsync<TBody, TResult>(
        string path, TBody body, JsonTypeInfo<TBody> bodyInfo, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, path, JsonContent.Create(body, bodyInfo), result, cancellationToken);

    public Task<TResult?> DeleteAsync<TResult>(string path, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, path, null, result, cancellationToken);

    public async Task<TResult?> SendAsync<TResult>(
        HttpMethod method, string path, HttpContent? content, JsonTypeInfo<TResult> result, CancellationToken cancellationToken = default)
    {
        var text = await SendForTextAsync(method, path, content, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(text, result);
        }
        catch (JsonException exception)
        {
            throw new ApiException(0, InvalidResponse, exception.Message);
        }
    }

    public async Task SendAsync(HttpMethod method, string path, HttpContent? content = null, CancellationToken cancellationToken = default) =>
        await SendForTextAsync(method, path, content, cancellationToken);

    public string Url(string path) => basePath + path;

    private async Task<string> SendForTextAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, Url(path)) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await SendOrThrowAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToApiExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOrThrowAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ApiException(0, ConnectionFailed, exception.Message);
        }
    }

    private static async Task<ApiException> ToApiExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var body = await TryReadErrorAsync(response, cancellationToken);
        var message = body?.Error ?? body?.Message ?? $"{status} {response.ReasonPhrase}";
        return new ApiException(status, message, body?.Details);
    }

    private static async Task<ApiErrorBody?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(ClientJsonContext.Default.ApiErrorBody, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
