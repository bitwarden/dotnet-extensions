using System.Text.Json.Serialization;

using Bitwarden.Core.Models;

namespace Bitwarden.Core.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Payload))]
[JsonSerializable(typeof(ProjectResponseModel))]
[JsonSerializable(typeof(SecretWithProjectsListResponseModel))]
[JsonSerializable(typeof(SecretResponseModel))]
[JsonSerializable(typeof(SecretUpdateRequestModel))]
public sealed partial class BitwardenSerializerContext : JsonSerializerContext
{

}
