using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// An expression read from a request may name only the fields its resource marks filterable,
/// wherever in the expression it names them; once checked, it carries the resource it was checked for.
/// </summary>
public sealed class ResourceFieldsTests
{
    [Fact]
    public void Require_FilterableFields_PassesAndRecordsTheResource()
    {
        var query = Read("""{ "and": [{ "==": [{ "var": "owner" }, "a"] }, { "!": { "in": [{ "var": "count" }, [1, 2]] } }] }""");

        ResourceFields.Require(query, typeof(Item));

        Assert.Equal(typeof(Item), query.Resource);
    }

    // Matched as the caller may write it, in any case.
    [Fact]
    public void Require_FieldInAnotherCase_Passes()
    {
        ResourceFields.Require(Read("""{ "==": [{ "var": "OWNER" }, "a"] }"""), typeof(Item));
    }

    [Fact]
    public void Require_FieldNotMarkedFilterable_IsRefusedListingTheFieldsAvailable()
    {
        var query = Read("""{ "==": [{ "var": "secret" }, "a"] }""");

        var actual = Assert.Throws<InvalidRequestException>(() => ResourceFields.Require(query, typeof(Item)));

        Assert.Equal("/filter", actual.Pointer);
        Assert.Contains("'secret' is not a field this resource may be filtered on", actual.Message, StringComparison.Ordinal);
        Assert.Contains("'count', 'owner'", actual.Message, StringComparison.Ordinal);
        Assert.Null(query.Resource);
    }

    [Fact]
    public void Require_FieldNestedDeepInTheExpression_IsStillChecked()
    {
        var query = Read("""{ "or": [{ "==": [{ "var": "owner" }, "a"] }, { "!": { "==": [{ "var": "secret" }, "a"] } }] }""");

        Assert.Throws<InvalidRequestException>(() => ResourceFields.Require(query, typeof(Item)));
    }

    [Fact]
    public void Require_ResourceWithNothingFilterable_SaysItCannotBeFilteredThisWay()
    {
        var actual = Assert.Throws<InvalidRequestException>(() =>
            ResourceFields.Require(Read("""{ "==": [{ "var": "secret" }, "a"] }"""), typeof(Unfilterable)));

        Assert.Contains("declares no fields", actual.Message, StringComparison.Ordinal);
    }

    private static Query Read(string filter) => JsonSerializer.Deserialize<Query>(filter)!;

    private sealed record Item : Resource
    {
        [Filterable]
        public string? Owner { get; init; }

        [Filterable]
        public int Count { get; init; }

        public string? Secret { get; init; }
    }

    private sealed record Unfilterable : Resource
    {
        public string? Secret { get; init; }
    }
}
