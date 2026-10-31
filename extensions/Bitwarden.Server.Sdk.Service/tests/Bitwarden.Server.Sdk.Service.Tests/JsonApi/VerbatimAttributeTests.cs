using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A verbatim member goes on properties, and is spared only the cleaning: its type is still
/// checked.
/// </summary>
/// <remarks>
/// How the binding keeps a verbatim string as sent is pinned in <see cref="ResourceDocumentBindingTests"/>.
/// </remarks>
public sealed class VerbatimAttributeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public void Usage_PropertiesOnly()
    {
        var usage = typeof(VerbatimAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
    }

    [Fact]
    public void Bind_VerbatimMemberOfTheWrongType_StillRefused()
    {
        var failure = ResourceDocumentBinding.TryBind(
            """{ "data": { "type": "note", "attributes": { "text": 7 } } }""",
            Options.GetTypeInfo(typeof(Note)), "note", new HashSet<string>(), out _);

        Assert.NotNull(failure);
        var error = Assert.Single(failure.Errors);
        Assert.Equal("invalid-type", error.Code);
        Assert.Equal("/data/attributes/text", error.Source?.Pointer);
    }

    public sealed record Note : Resource
    {
        [Verbatim]
        public string? Text { get; init; }
    }
}
