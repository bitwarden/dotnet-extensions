using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The JSON:API envelope always carries <c>attributes</c>, which the published schema requires.
/// </summary>
public sealed class ResourceEnvelopeTests
{
    [Fact]
    public void Envelope_NoMemberSet_WritesAnEmptyAttributesObject()
    {
        var flattened = new JsonObject { ["id"] = "1", ["type"] = "probe" };

        var actual = ResourceEnvelope.Envelope(flattened);

        var attributes = Assert.IsType<JsonObject>(actual["attributes"]);
        Assert.Empty(attributes);
    }

    [Fact]
    public void Envelope_MembersSet_MovesThemIntoAttributes()
    {
        var flattened = new JsonObject { ["id"] = "1", ["type"] = "probe", ["note"] = "n" };

        var actual = ResourceEnvelope.Envelope(flattened);

        Assert.Equal("n", actual["attributes"]!["note"]!.GetValue<string>());
        Assert.Null(actual["note"]);
    }

    [Fact]
    public void Sparse_NoAskedForAttributeSet_KeepsAnEmptyAttributesObject()
    {
        var enveloped = ResourceEnvelope.Envelope(new JsonObject { ["id"] = "1", ["type"] = "probe", ["note"] = "n" });

        var actual = ResourceEnvelope.Sparse(enveloped, new HashSet<string> { "other" });

        var attributes = Assert.IsType<JsonObject>(actual["attributes"]);
        Assert.Empty(attributes);
    }
}
