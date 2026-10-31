namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// [AcceptsAssertedContext]: which calling services an operation lets assert a person on their own authority, by their
/// internal client name and nothing looser.
/// </summary>
public sealed class AcceptsAssertedContextAttributeTests
{
    private static readonly AcceptsAssertedContextAttribute Declared =
        new("The service's own calls about a person.", "identity-service", "events-service");

    [Fact]
    public void Constructor_KeepsTheReasonAndTheCallers()
    {
        Assert.Equal("The service's own calls about a person.", Declared.Reason);
        Assert.Equal(["identity-service", "events-service"], Declared.Callers);
    }

    [Theory]
    [InlineData("internal.identity-service")]
    [InlineData("internal.events-service")]
    public void Admits_ANamedServicesInternalClient_True(string client)
    {
        Assert.True(Declared.Admits(client));
    }

    // Only the internal client of a named service: not the bare name, another service, a near miss in case, or nobody.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("identity-service")]
    [InlineData("internal.")]
    [InlineData("internal.other-service")]
    [InlineData("internal.Identity-Service")]
    [InlineData("Internal.identity-service")]
    [InlineData("internal.identity-service.extra")]
    [InlineData("web")]
    public void Admits_AnyOtherClient_False(string? client)
    {
        Assert.False(Declared.Admits(client));
    }

    [Fact]
    public void Admits_NamingNoService_AdmitsNobody()
    {
        Assert.False(new AcceptsAssertedContextAttribute("No caller named.").Admits("internal.identity-service"));
    }

    [Fact]
    public void TheDeclaration_IsMadeOnceOnAnOperation()
    {
        var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
            typeof(AcceptsAssertedContextAttribute), typeof(AttributeUsageAttribute))!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }
}
