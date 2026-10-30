namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class IServiceIdentityTests
{
    [Fact]
    public void Defaults_AreADomainServiceAcceptingThePlatform()
    {
        IServiceIdentity identity = new DomainIdentity();

        Assert.False(identity.IsProductService);
        Assert.Equal(TokenAudience.Platform, identity.Audience);
    }

    [Fact]
    public void Audience_OfAProductService_IsItsOwnName()
    {
        IServiceIdentity identity = new ProductIdentity();

        Assert.Equal("widget-service", identity.Audience);
    }

    private sealed class DomainIdentity : IServiceIdentity
    {
        public string Name => "widget-service";

        public string Title => "Widget Service";
    }

    private sealed class ProductIdentity : IServiceIdentity
    {
        public string Name => "widget-service";

        public string Title => "Widget Service";

        public bool IsProductService => true;
    }
}
