using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Idempotency;

[JsonSerializable(typeof(IdempotentWrites.Record))]
internal sealed partial class IdempotencyJsonContext : JsonSerializerContext;
