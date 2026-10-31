using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// The request-level checks every bulk operation makes before touching a resource, and the version
/// check every update makes.
/// </summary>
public sealed class BulkRequestsTests
{
    [Fact]
    public void RequireAtMost_AtTheMaximum_Passes()
    {
        BulkRequests.RequireAtMost(500, 500, "/ids");
    }

    [Fact]
    public void RequireAtMost_OverTheMaximum_RefusesAtThePointer()
    {
        var refusal = Assert.Throws<InvalidRequestException>(() => BulkRequests.RequireAtMost(501, 500, "/data"));

        Assert.Equal("/data", refusal.Pointer);
    }

    [Fact]
    public void RequireDistinct_SameIdInAnotherCase_RefusesAtItsSecondAppearance()
    {
        var refusal = Assert.Throws<InvalidRequestException>(() => BulkRequests.RequireDistinct(
            ["7b1e0000-0000-4000-8000-0000000000aa", "other", "7B1E0000-0000-4000-8000-0000000000AA"]));

        Assert.Equal("/ids/2", refusal.Pointer);
    }

    [Fact]
    public void RequireDistinct_Distinct_Passes()
    {
        BulkRequests.RequireDistinct(["a", "b"]);
    }

    [Fact]
    public void BulkRefusedException_CarriesEveryError()
    {
        var refused = new BulkRefusedException(
        [
            new() { Status = "422", Code = "invalid-value", Title = "Invalid value" },
            new() { Status = "422", Code = "invalid-value", Title = "Invalid value" },
        ]);

        Assert.Equal(2, refused.Errors.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    public void Concurrency_NoVersionOrTheStoredOne_Passes(string? sent)
    {
        Concurrency.Require(sent, 3);
    }

    [Fact]
    public void Concurrency_AnotherVersion_Conflicts()
    {
        Assert.Throws<ConflictException>(() => Concurrency.Require("2", 3));
    }
}
