using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for negotiation wire types. Keeps the
/// transport implementations trim- and AOT-safe.
/// </summary>
[JsonSerializable(typeof(Capability))]
[JsonSerializable(typeof(PublisherJoin))]
[JsonSerializable(typeof(NegotiationAck))]
internal partial class NegotiationJsonContext : JsonSerializerContext;
