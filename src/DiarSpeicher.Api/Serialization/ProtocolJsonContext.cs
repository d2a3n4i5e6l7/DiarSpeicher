using System.Text.Json;
using System.Text.Json.Serialization;
using DiarSpeicher.Api.Endpoints;
using DiarSpeicher.Core.Domain.MangaBaka;
using DiarSpeicher.Core.Domain.Opds;
using DiarSpeicher.Core.Domain.Sync;

namespace DiarSpeicher.Api.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(KoboInitializationResponse))]
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
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
