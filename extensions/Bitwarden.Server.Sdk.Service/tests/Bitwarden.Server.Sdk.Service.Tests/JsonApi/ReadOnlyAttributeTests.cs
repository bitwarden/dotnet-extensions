using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A read-only member goes on properties, is dropped from a request rather than refused, and is
/// never required of a caller.
/// </summary>
public sealed class ReadOnlyAttributeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public void Usage_PropertiesOnly()
    {
        var usage = typeof(ReadOnlyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
    }

    [Fact]
    public void Bind_ReadOnlyMemberSent_DroppedNotRefused()
    {
        var failure = ResourceDocumentBinding.TryBind(
            """{ "data": { "type": "gauge", "attributes": { "reading": 3, "label": "fake" } } }""",
            Options.GetTypeInfo(typeof(Gauge)), "gauge", new HashSet<string>(), out var document);

        Assert.Null(failure);
        var attributes = document!["data"]!["attributes"]!.AsObject();
        Assert.False(attributes.ContainsKey("reading"));
        Assert.Equal("fake", attributes["label"]!.GetValue<string>());
    }

    // A non-nullable member would otherwise be required; a read-only one is the server's to set.
    [Fact]
    public void Bind_ReadOnlyMemberAbsent_NotRequired()
    {
        var failure = ResourceDocumentBinding.TryBind(
            """{ "data": { "type": "gauge", "attributes": {} } }""",
            Options.GetTypeInfo(typeof(Gauge)), "gauge", new HashSet<string>(), out _);

        Assert.Null(failure);
    }

    public sealed record Gauge : Resource
    {
        [ReadOnly]
        public int Reading { get; init; }

        public string? Label { get; init; }
    }
}
