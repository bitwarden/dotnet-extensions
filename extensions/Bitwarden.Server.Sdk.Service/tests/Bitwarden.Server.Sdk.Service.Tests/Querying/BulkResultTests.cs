using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// What a bulk operation answers: how many resources it changed, and for a best-effort one how
/// many it did not, with an error for each.
/// </summary>
public sealed class BulkResultTests
{
    [Fact]
    public void Affected_IsTheCountAlone()
    {
        var actual = BulkResult.Affected(4);

        Assert.Equal(4, actual.Meta["affectedCount"]);
        Assert.Single(actual.Meta);
        Assert.Null(actual.Errors);
    }

    [Fact]
    public void BestEffort_WithFailures_CountsThemAndCarriesEach()
    {
        Error[] failures = [new() { Status = "404" }, new() { Status = "409" }];

        var actual = BulkResult.BestEffort(3, failures);

        Assert.Equal(3, actual.Meta["affectedCount"]);
        Assert.Equal(2, actual.Meta["failedCount"]);
        Assert.Equal(failures, actual.Errors);
    }

    // No empty errors member: a document with one is read as having failed.
    [Fact]
    public void BestEffort_NoFailures_HasNoErrors()
    {
        var actual = BulkResult.BestEffort(3, []);

        Assert.Equal(0, actual.Meta["failedCount"]);
        Assert.Null(actual.Errors);
    }

    [Fact]
    public void BestEffort_NoFailureList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BulkResult.BestEffort(0, null!));
    }

    [Fact]
    public void Write_IsAMetaDocument()
    {
        var actual = JsonNode.Parse(JsonSerializer.Serialize(BulkResult.Affected(2), JsonSerializerOptions.Web))!;

        Assert.Equal(2, actual["meta"]!["affectedCount"]!.GetValue<int>());
    }
}
