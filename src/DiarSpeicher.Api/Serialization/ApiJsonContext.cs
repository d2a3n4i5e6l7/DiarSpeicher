using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Core.Domain.Models;

namespace DiarSpeicher.Api.Serialization;

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
internal sealed partial class ApiJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiarSpeicherScanStatusDto))]
internal sealed partial class ScanEventJsonContext : JsonSerializerContext;

/// <summary>
/// Whatever is stored on disk or in the database keeps the default options (PascalCase) it was
/// always written with: changing them would make saved upload sessions and profiles unreadable.
/// </summary>
[JsonSerializable(typeof(TusEndpoints.TusUploadMeta))]
[JsonSerializable(typeof(List<EpubDeviceProfile>))]
internal sealed partial class PersistedJsonContext : JsonSerializerContext;
