
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Tests.Querying;

/// <summary>
/// The request-level checks every bulk operation makes before touching a resource.
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
}
