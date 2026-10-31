namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A bulk operation's route is absolute, a suffix on its collection, and a bulk action's name
/// cannot collide with another bulk operation's.
/// </summary>
public sealed class BulkRoutesTests
{
    [Theory]
    [InlineData("api/v1/items")]
    [InlineData("/api/v1/items")]
    [InlineData("api/v1/items/")]
    [InlineData("//api/v1/items//")]
    public void For_Collection_AbsoluteSuffix(string collection)
    {
        Assert.Equal("~/api/v1/items:bulk-update", BulkRoutes.For(collection, "bulk-update"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void For_BlankCollection_Refused(string? collection)
    {
        Assert.ThrowsAny<ArgumentException>(() => BulkRoutes.For(collection!, "search"));
    }

    [Fact]
    public void ForAction_Name_KebabCasedAfterBulk()
    {
        Assert.Equal("~/api/v1/members:bulk-send-invite", BulkRoutes.ForAction("api/v1/members/", "SendInvite"));
    }

    // Kebab-cased first, so a name differing only in case is caught too.
    [Theory]
    [InlineData("create")]
    [InlineData("Update")]
    [InlineData("REPLACE")]
    [InlineData("delete")]
    public void ForAction_ReservedName_RefusedNamingTheRoute(string action)
    {
        var actual = Assert.Throws<InvalidOperationException>(() => BulkRoutes.ForAction("/api/v1/members/", action));

        var name = action.ToLowerInvariant();
        Assert.Equal(
            $"'{name}' cannot be the name of a bulk action: it would publish api/v1/members:bulk-{name}, which is "
            + $"the bulk {name} operation. Bulk actions share a namespace with the other bulk operations.",
            actual.Message);
    }

    // Only the whole name is reserved.
    [Fact]
    public void ForAction_NameContainingAReservedOne_Allowed()
    {
        Assert.Equal("~/api/v1/members:bulk-delete-devices", BulkRoutes.ForAction("api/v1/members", "DeleteDevices"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ForAction_BlankAction_Refused(string? action)
    {
        Assert.ThrowsAny<ArgumentException>(() => BulkRoutes.ForAction("api/v1/members", action!));
    }
}
