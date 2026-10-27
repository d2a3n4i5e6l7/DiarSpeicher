using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DiarSpeicher.Client.Api;
using Microsoft.AspNetCore.Components.Forms;

namespace DiarSpeicher.Client.Upload;

public enum TusUploadStatus
{
    Idle,
    Uploading,
    Paused,
    Completed,
    Error,
}

public sealed class TusUpload(HttpClient http, GatewayOptions gateway, IBrowserFile file, string libraryId, string? subpath)
{
    private const string TusVersion = "1.0.0";
    private const string OffsetHeader = "Upload-Offset";
    private const int PercentScale = 100;

    private const int ChunkSize = 10 * 1024 * 1024;

    private string? _uploadUrl;
    private CancellationTokenSource? _transfer;

    public event Action? Changed;

    public IBrowserFile File => file;

    public TusUploadStatus Status { get; private set; } = TusUploadStatus.Idle;

    public long Offset { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int Percentage => file.Size == 0 ? PercentScale : (int)Math.Min(PercentScale, Math.Round(Offset * (double)PercentScale / file.Size));

    public async Task StartAsync()
    {
        if (Status is TusUploadStatus.Completed or TusUploadStatus.Uploading)
        {
            return;
        }

        _transfer = new CancellationTokenSource();
        var token = _transfer.Token;
        SetStatus(TusUploadStatus.Uploading, error: null);
        try
        {
            if (_uploadUrl is null)
            {
                _uploadUrl = await CreateAsync(token);
                Offset = 0;
            }
            else
            {
                Offset = await ServerOffsetAsync(_uploadUrl, token);
            }

            await SendChunksAsync(_uploadUrl, token);
            if (Offset >= file.Size)
            {
                SetStatus(TusUploadStatus.Completed, error: null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            SetStatus(TusUploadStatus.Error, exception.Message);
        }
    }

    public void Pause()
    {
        if (Status != TusUploadStatus.Uploading)
        {
            return;
        }

        _transfer?.Cancel();
        SetStatus(TusUploadStatus.Paused, error: null);
    }

    public async Task CancelAsync()
    {
        _transfer?.Cancel();
        SetStatus(TusUploadStatus.Idle, error: null);
        if (_uploadUrl is not null)
        {
            try
            {
                using var request = Request(HttpMethod.Delete, _uploadUrl);
                using var response = await http.SendAsync(request);
            }
            catch (HttpRequestException)
            {
            }

            _uploadUrl = null;
        }

        Offset = 0;
    }

    private async Task<string> CreateAsync(CancellationToken token)
    {
        List<string> metadata = [$"filename {Encode(file.Name)}", $"libraryId {Encode(libraryId)}"];
        if (!string.IsNullOrWhiteSpace(subpath))
        {
            metadata.Add($"subpath {Encode(subpath)}");
        }

        using var request = Request(HttpMethod.Post, $"{gateway.UrlAccess}/api/v2/files");
        request.Headers.TryAddWithoutValidation("Upload-Length", file.Size.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Upload-Metadata", string.Join(',', metadata));
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Fallo al iniciar subida TUS: HTTP {(int)response.StatusCode}");
        }

        var location = response.Headers.Location?.OriginalString
            ?? throw new InvalidOperationException("El servidor no devolvió la cabecera Location para la subida TUS");
        return location.StartsWith("http", StringComparison.OrdinalIgnoreCase) || location.StartsWith(gateway.UrlAccess, StringComparison.Ordinal)
            ? location
            : gateway.UrlAccess + location;
    }

    private async Task<long> ServerOffsetAsync(string uploadUrl, CancellationToken token)
    {
        using var request = Request(HttpMethod.Head, uploadUrl);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"No se pudo verificar el offset TUS: HTTP {(int)response.StatusCode}");
        }

        return ReadOffset(response) ?? 0;
    }

    private async Task SendChunksAsync(string uploadUrl, CancellationToken token)
    {
        while (Offset < file.Size)
        {
            await SendFromOffsetAsync(uploadUrl, token);
        }
    }

    private async Task SendFromOffsetAsync(string uploadUrl, CancellationToken token)
    {
        await using var stream = file.OpenReadStream(long.MaxValue, token);
        await SkipAsync(stream, Offset, token);
        var buffer = new byte[ChunkSize];
        while (Offset < file.Size)
        {
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, token);
            if (read == 0)
            {
                throw new InvalidOperationException("El fichero terminó antes de lo que dice su tamaño.");
            }

            var expected = Offset + read;
            await SendChunkAsync(uploadUrl, buffer, read, token);
            Changed?.Invoke();
            if (Offset != expected)
            {
                return;
            }
        }
    }

    private async Task SendChunkAsync(string uploadUrl, byte[] buffer, int count, CancellationToken token)
    {
        using var request = Request(HttpMethod.Patch, uploadUrl);
        request.Headers.TryAddWithoutValidation(OffsetHeader, Offset.ToString(CultureInfo.InvariantCulture));
        request.Content = new ByteArrayContent(buffer, 0, count);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        using var response = await http.SendAsync(request, token);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NoContent:
                Offset = ReadOffset(response) ?? Offset + count;
                break;
            case HttpStatusCode.Conflict:
                Offset = ReadOffset(response) ?? Offset;
                break;
            default:
                throw new InvalidOperationException($"Error enviando bloque TUS: HTTP {(int)response.StatusCode}");
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Tus-Resumable", TusVersion);
        return request;
    }

    private static long? ReadOffset(HttpResponseMessage response) =>
        response.Headers.TryGetValues(OffsetHeader, out var values)
        && long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : null;

    private static async Task SkipAsync(Stream stream, long bytes, CancellationToken token)
    {
        var scratch = new byte[ChunkSize];
        while (bytes > 0)
        {
            var read = await stream.ReadAsync(scratch.AsMemory(0, (int)Math.Min(scratch.Length, bytes)), token);
            if (read == 0)
            {
                return;
            }

            bytes -= read;
        }
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private void SetStatus(TusUploadStatus status, string? error)
    {
        Status = status;
        ErrorMessage = error;
        Changed?.Invoke();
    }
}
