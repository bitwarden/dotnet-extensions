using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.WebEssentials;

[JsonSerializable(typeof(string))]
internal sealed partial class WebEssentialsJsonSerializerContext : JsonSerializerContext { }
