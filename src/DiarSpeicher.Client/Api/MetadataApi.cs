using System.Net.Http.Headers;
using DiarSpeicher.Core.Domain.MangaBaka;
using Microsoft.AspNetCore.Components.Forms;

namespace DiarSpeicher.Client.Api;

public sealed class MetadataApi(HttpClient http, GatewayOptions gateway)
{
    private const int PercentScale = 100;

    private readonly ApiClient _client = new(http, gateway.UrlAccess);

    public Task<MangaBakaStatusDto?> StatusAsync() =>
        _client.GetAsync("/api/v2/metadata/status", CatalogJsonContext.Default.MangaBakaStatusDto);

    public Task DownloadAsync() => _client.SendAsync(HttpMethod.Post, "/api/v2/metadata/download");

    public async Task ImportChunkedAsync(IBrowserFile file, long maxFileBytes, Action<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var init = await _client.PostAsync("/api/v2/metadata/import/init",
            new MetadataImportInitRequest(file.Name, file.Size),
            CatalogJsonContext.Default.MetadataImportInitRequest, CatalogJsonContext.Default.MetadataImportInitResponse, cancellationToken)
            ?? throw new ApiException(0, "El servidor no abrió la importación");

        await using var stream = file.OpenReadStream(maxFileBytes, cancellationToken);
        var buffer = new byte[init.ChunkSize];
        long offset = 0;
        while (offset < file.Size)
        {
            var read = await ReadChunkAsync(stream, buffer, cancellationToken);
            using var content = new ByteArrayContent(buffer, 0, read);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var result = await _client.SendAsync(HttpMethod.Post,
                $"/api/v2/metadata/import/chunk?uploadId={Uri.EscapeDataString(init.UploadId)}&offset={offset}",
                content, CatalogJsonContext.Default.MetadataChunkResponse, cancellationToken);
            offset = result?.Offset ?? offset + read;
            onProgress?.Invoke((int)Math.Min(PercentScale, Math.Round(offset * (double)PercentScale / file.Size)));
        }

        await _client.PostAsync("/api/v2/metadata/import/complete",
            new MetadataImportCompleteRequest(init.UploadId, file.Name),
            CatalogJsonContext.Default.MetadataImportCompleteRequest, CatalogJsonContext.Default.UpdatedResult, cancellationToken);
    }

    public Task<List<MangaBakaCandidateDto>?> CandidatesAsync(string seriesId, int limit = 10) =>
        _client.GetAsync($"/api/v2/metadata/series/{seriesId}/candidates?limit={limit}", CatalogJsonContext.Default.ListMangaBakaCandidateDto);

    public Task<MatchedResult?> MatchAsync(string seriesId, long mangaBakaId) =>
        _client.PutAsync($"/api/v2/metadata/series/{seriesId}/match/{mangaBakaId}", CatalogJsonContext.Default.MatchedResult);

    public Task UnmatchAsync(string seriesId) => _client.SendAsync(HttpMethod.Delete, $"/api/v2/metadata/series/{seriesId}/match");

    public string CoverUrl(string externalUrl) => _client.Url($"/api/v2/metadata/cover?url={Uri.EscapeDataString(externalUrl)}");

    private static async Task<int> ReadChunkAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
