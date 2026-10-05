using System.Diagnostics.CodeAnalysis;
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

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Resolver is only instantiated if IsReflectionEnabledByDefault is set")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Resolver is only instantiated if IsReflectionEnabledByDefault is set")]
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            return new JsonSerializerOptions();
        }

        return new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    }
}
