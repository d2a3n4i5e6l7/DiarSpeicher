using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Komga;
using DiarSpeicher.Core.Domain.MangaBaka;
using DiarSpeicher.Core.Domain.Models;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Domain.Sync;
using DiarSpeicher.Infrastructure.Filesystem;

namespace DiarSpeicher.Api.Serialization;

/// <summary>
/// Resultados como Results.Ok serializan con el tipo de ejecucion cuando el declarado es una
/// interfaz u object, asi que junto a IReadOnlyList se registran List y arreglo, y string para
/// los valores de los diccionarios de object.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(OffsetConflictError))]
[JsonSerializable(typeof(ServiceHealthResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(VersionResponse))]
[JsonSerializable(typeof(ClaimStatusResponse))]
[JsonSerializable(typeof(CurrentUserResponse))]
[JsonSerializable(typeof(UpdatedResponse))]
[JsonSerializable(typeof(MatchedResponse))]
[JsonSerializable(typeof(RemovedResponse))]
[JsonSerializable(typeof(EpubProfilesUpdatedResponse))]
[JsonSerializable(typeof(ChunkedUploadSessionResponse))]
[JsonSerializable(typeof(UploadOffsetResponse))]
[JsonSerializable(typeof(KoboInitializationResponse))]
[JsonSerializable(typeof(KomgaCurrentUserResponse))]
[JsonSerializable(typeof(List<FolderRootDto>))]
[JsonSerializable(typeof(FolderIndexStatus))]
[JsonSerializable(typeof(IReadOnlyList<FolderHit>))]
[JsonSerializable(typeof(List<FolderHit>))]
[JsonSerializable(typeof(FolderHit[]))]
[JsonSerializable(typeof(DeletionPreviewDto))]
[JsonSerializable(typeof(TrashEntryDto))]
[JsonSerializable(typeof(List<TrashEntryDto>))]
[JsonSerializable(typeof(DiskUsageDto))]
[JsonSerializable(typeof(FolderListingDto))]
[JsonSerializable(typeof(ScanPreviewDto))]
[JsonSerializable(typeof(FolderEntryDto))]
[JsonSerializable(typeof(CreateFolderInput))]
[JsonSerializable(typeof(List<KomgaLibraryDto>))]
[JsonSerializable(typeof(KomgaLibraryDto))]
[JsonSerializable(typeof(KomgaPageResponse<KomgaSeriesDto>))]
[JsonSerializable(typeof(KomgaSeriesDto))]
[JsonSerializable(typeof(KomgaPageResponse<KomgaBookDto>))]
[JsonSerializable(typeof(KomgaBookDto))]
[JsonSerializable(typeof(List<KomgaBookPageDto>))]
[JsonSerializable(typeof(KomgaReadProgressUpdateDto))]
[JsonSerializable(typeof(DiarSpeicherPageResponse<DiarSpeicherMediaDto>))]
[JsonSerializable(typeof(List<DiarSpeicherMediaDto>))]
[JsonSerializable(typeof(DiarSpeicherMediaDto))]
[JsonSerializable(typeof(DiarSpeicherPageResponse<DiarSpeicherSeriesDto>))]
[JsonSerializable(typeof(DiarSpeicherSeriesDto))]
[JsonSerializable(typeof(List<DiarSpeicherLibraryDto>))]
[JsonSerializable(typeof(DiarSpeicherLibraryDto))]
[JsonSerializable(typeof(DiarSpeicherUpdateProgressInput))]
[JsonSerializable(typeof(DiarSpeicherUpdateSeriesInput))]
[JsonSerializable(typeof(DiarSpeicherSeriesThumbnailInput))]
[JsonSerializable(typeof(DiarSpeicherCreateLibraryInput))]
[JsonSerializable(typeof(DiarSpeicherUpdateLibraryInput))]
[JsonSerializable(typeof(DiarSpeicherMissingReportDto))]
[JsonSerializable(typeof(DiarSpeicherScanStatusDto))]
[JsonSerializable(typeof(List<DiarSpeicherScanStatusDto>))]
[JsonSerializable(typeof(DiarSpeicherUploadResponseDto))]
[JsonSerializable(typeof(DiarSpeicherEpubTocDto))]
[JsonSerializable(typeof(List<EpubDeviceProfile>))]
[JsonSerializable(typeof(KoReaderProgressInput))]
[JsonSerializable(typeof(KoReaderAuthResponse))]
[JsonSerializable(typeof(KoReaderProgressResponse))]
[JsonSerializable(typeof(KoReaderPutProgressResponse))]
[JsonSerializable(typeof(List<KoboBookEntitlementContainer>))]
[JsonSerializable(typeof(KoboBookMetadata))]
[JsonSerializable(typeof(IReadOnlyList<MangaBakaCandidateDto>))]
[JsonSerializable(typeof(List<MangaBakaCandidateDto>))]
[JsonSerializable(typeof(MangaBakaCandidateDto[]))]
[JsonSerializable(typeof(MangaBakaStatusDto))]
[JsonSerializable(typeof(MetadataEndpoints.MetadataImportInitRequest))]
[JsonSerializable(typeof(MetadataEndpoints.MetadataImportCompleteRequest))]
[JsonSerializable(typeof(OpdsV2AuthenticationDoc))]
[JsonSerializable(typeof(OpdsV2Feed))]
[JsonSerializable(typeof(OpdsV2Publication))]
[JsonSerializable(typeof(OpdsV2Progression))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiarSpeicherScanStatusDto))]
internal sealed partial class ScanEventJsonContext : JsonSerializerContext;

/// <summary>
/// Lo que se guarda en disco o en la base mantiene las opciones por defecto (PascalCase) con que
/// se escribio siempre: cambiarlas dejaria ilegibles las sesiones de subida y los perfiles guardados.
/// </summary>
[JsonSerializable(typeof(TusEndpoints.TusUploadMeta))]
[JsonSerializable(typeof(List<EpubDeviceProfile>))]
internal sealed partial class PersistedJsonContext : JsonSerializerContext;
