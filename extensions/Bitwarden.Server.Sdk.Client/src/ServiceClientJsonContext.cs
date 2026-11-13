using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Serialization metadata for the documents this package reads itself.
/// </summary>
/// <remarks>
/// Source-generated rather than reflection-based, so a consumer may trim or ahead-of-time
/// compile. A client library has no business deciding that for the application hosting it.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IssuedToken))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(IssuerDocument))]
internal sealed partial class ServiceClientJsonContext : JsonSerializerContext;
