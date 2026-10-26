using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Core.Domain.Catalog;
using DiarSpeicher.Infrastructure.Filesystem;

namespace DiarSpeicher.Api.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
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
internal sealed partial class FilesystemJsonContext : JsonSerializerContext;
