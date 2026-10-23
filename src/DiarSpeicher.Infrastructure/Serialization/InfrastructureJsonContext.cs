using System.Text.Json.Serialization;

namespace DiarSpeicher.Infrastructure.Serialization;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(TrashEntry))]
[JsonSerializable(typeof(PartialUploadPurgeService.UploadMeta))]
internal sealed partial class InfrastructureJsonContext : JsonSerializerContext;
