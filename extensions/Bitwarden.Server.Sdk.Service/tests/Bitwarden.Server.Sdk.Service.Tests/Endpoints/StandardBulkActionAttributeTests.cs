using System.Reflection;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A bulk action is a <c>POST</c> to <c>{collection}:bulk-{action}</c>, named for the method it is
/// on unless it says otherwise, and may not take the name of another bulk operation.
/// </summary>
public sealed class StandardBulkActionAttributeTests
{
    [Fact]
    public void Action_Given_PostToItsSuffix()
    {
        var actual = new StandardBulkActionAttribute("api/v1/members", "Confirm");

        Assert.Equal(["POST"], actual.HttpMethods);
        Assert.Equal("~/api/v1/members:bulk-confirm", actual.Template);
        Assert.Equal("confirm", actual.Action);
        Assert.Equal(200, actual.SuccessStatusCode);
        Assert.False(((IStandardOperation)actual).IsRead);
    }

    [Fact]
    public void Action_NotGiven_IsTheMethodsName()
    {
        var actual = typeof(Handlers).GetMethod(nameof(Handlers.RevokeAccess))!
            .GetCustomAttribute<StandardBulkActionAttribute>()!;

        Assert.Equal("revoke-access", actual.Action);
        Assert.Equal("~/api/v1/members:bulk-revoke-access", actual.Template);
    }

    // Each would publish the route of the bulk operation it is named for.
    [Theory]
    [InlineData("create")]
    [InlineData("Update")]
    [InlineData("replace")]
    [InlineData("Delete")]
    public void Action_NameOfAnotherBulkOperation_Refused(string action)
    {
        var actual = Assert.Throws<InvalidOperationException>(() => new StandardBulkActionAttribute("api/v1/members", action));

        Assert.Contains($"'{action.ToLowerInvariant()}' cannot be the name of a bulk action", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Action_Blank_Refused(string action)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StandardBulkActionAttribute("api/v1/members", action));
    }

    [Fact]
    public void Defaults_AllOrNothingTargetedEitherWay()
    {
        var actual = new StandardBulkActionAttribute("api/v1/members", "confirm");

        Assert.Equal(Atomicity.AllOrNothing, actual.Supports);
        Assert.Equal(Targeting.Either, actual.Targeting);
    }

    [Fact]
    public void Declared_Set_IsKept()
    {
        var actual = new StandardBulkActionAttribute("api/v1/members", "confirm")
        {
            Supports = Atomicity.BestEffort,
            Targeting = Targeting.Filter,
        };

        Assert.Equal(Atomicity.BestEffort, actual.Supports);
        Assert.Equal(Targeting.Filter, actual.Targeting);
    }

    private static class Handlers
    {
        [StandardBulkAction("api/v1/members")]
        public static void RevokeAccess()
        {
        }
    }
}
