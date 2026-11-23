using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Resources.Tests;

/// <summary>
/// The envelope always carries <c>attributes</c>, which the published schema requires, and only
/// <c>id</c> and <c>type</c> are read from outside it.
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

    // A member beside attributes would otherwise reach the resource without the cleaning binding
    // gives attributes, a read-only one included.
    [Theory]
    [InlineData("createdBy")]
    [InlineData("CreatedBy")]
    [InlineData("ID")]
    [InlineData("Type")]
    [InlineData("name")]
    public void Flatten_AMemberBesideAttributes_Dropped(string member)
    {
        var enveloped = new JsonObject
        {
            ["id"] = "1",
            ["type"] = "probe",
            [member] = "x",
            ["attributes"] = new JsonObject { ["note"] = "n" },
        };

        var actual = ResourceEnvelope.Flatten(enveloped);

        Assert.Equal(["note", "id", "type"], actual.Select(pair => pair.Key));
        Assert.Equal("1", actual["id"]!.GetValue<string>());
        Assert.Equal("probe", actual["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("ID")]
    [InlineData("type")]
    public void Flatten_AnIdentityInsideAttributes_Dropped(string member)
    {
        var enveloped = new JsonObject
        {
            ["type"] = "probe",
            ["attributes"] = new JsonObject { [member] = "x", ["note"] = "n" },
        };

        var actual = ResourceEnvelope.Flatten(enveloped);

        Assert.Equal(["note", "type"], actual.Select(pair => pair.Key));
        Assert.Equal("probe", actual["type"]!.GetValue<string>());
    }
}
