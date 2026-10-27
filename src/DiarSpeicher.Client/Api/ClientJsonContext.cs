using System.Text.Json.Serialization;

namespace DiarSpeicher.Client.Api;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApiErrorBody))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(List<UserItem>))]
[JsonSerializable(typeof(UserItem))]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(UpdateUserRequest))]
[JsonSerializable(typeof(ProtocolCatalog))]
[JsonSerializable(typeof(UpdatedResult))]
[JsonSerializable(typeof(DeletedResult))]
[JsonSerializable(typeof(SuccessResult))]
internal sealed partial class ClientJsonContext : JsonSerializerContext;
