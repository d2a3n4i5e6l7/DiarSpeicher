using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Api.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
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
internal sealed partial class CatalogJsonContext : JsonSerializerContext;
