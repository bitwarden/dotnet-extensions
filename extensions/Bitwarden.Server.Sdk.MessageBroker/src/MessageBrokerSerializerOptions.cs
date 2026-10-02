using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Options for configuring message broker serialization.</summary>
public sealed class MessageBrokerSerializerOptions
{
    /// <summary>The <see cref="JsonSerializerOptions"/> to use for serialization and deserialization.</summary>
    /// <remarks>
    /// Configure this with a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> to enable AOT-compatible serialization.
    /// </remarks>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = CreateDefaultOptions();

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return new JsonSerializerOptions();
        }

#pragma warning disable IL2026, IL3050
        return new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
#pragma warning restore IL2026, IL3050
    }
}
