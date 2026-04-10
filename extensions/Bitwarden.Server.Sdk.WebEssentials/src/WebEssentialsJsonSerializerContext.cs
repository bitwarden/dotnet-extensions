using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.WebEssentials;

[JsonSerializable(typeof(VersionResponse))]
internal sealed partial class WebEssentialsJsonSerializerContext : JsonSerializerContext { }
