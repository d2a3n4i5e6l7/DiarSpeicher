using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Core.Domain.Komga;

namespace DiarSpeicher.Api.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(KomgaCurrentUserResponse))]
[JsonSerializable(typeof(List<KomgaLibraryDto>))]
[JsonSerializable(typeof(KomgaLibraryDto))]
[JsonSerializable(typeof(KomgaPageResponse<KomgaSeriesDto>))]
[JsonSerializable(typeof(KomgaSeriesDto))]
[JsonSerializable(typeof(KomgaPageResponse<KomgaBookDto>))]
[JsonSerializable(typeof(KomgaBookDto))]
[JsonSerializable(typeof(List<KomgaBookPageDto>))]
[JsonSerializable(typeof(KomgaReadProgressUpdateDto))]
[JsonSerializable(typeof(KomgaTachiyomiProgressDto))]
[JsonSerializable(typeof(KomgaTachiyomiProgressUpdateDto))]
internal sealed partial class KomgaJsonContext : JsonSerializerContext;
