namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// Admits only the internal clients of the services an operation names, so each asserted context naming a person is a
/// reviewed, named exception: a stolen credential asserts a person only where its own service was let.
/// </summary>
public sealed class AcceptsAssertedContextAttributeTests
{
    private static readonly AcceptsAssertedContextAttribute Accepts =
        new("Background work about a person.", "widget-service", "gadget-service");

    [Theory]
    [InlineData("internal.widget-service")]
    [InlineData("internal.gadget-service")]
    public void Admits_ANamedServicesInternalClient_True(string client)
    {
        Assert.True(Accepts.Admits(client));
    }

    // Only the internal client of a named service, by its exact name.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("internal.")]
    [InlineData("widget-service")]
    [InlineData("internal.another-service")]
    [InlineData("internal.widget")]
    [InlineData("internal.widget-service-two")]
    [InlineData("internal.Widget-service")]
    [InlineData("Internal.widget-service")]
    [InlineData("web.widget-service")]
    public void Admits_AnyOtherClient_False(string? client)
    {
        Assert.False(Accepts.Admits(client));
    }

    [Fact]
    public void Admits_NoServicesNamed_NobodyAdmitted()
    {
        Assert.False(new AcceptsAssertedContextAttribute("Background work about a person.").Admits("internal.widget-service"));
    }
}
