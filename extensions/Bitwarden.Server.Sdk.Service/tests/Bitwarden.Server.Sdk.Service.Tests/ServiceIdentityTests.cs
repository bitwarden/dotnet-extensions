using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// Which service this process is: a domain service unless marked otherwise, addressed by its name.
/// </summary>
public sealed class ServiceIdentityTests
{
    [Fact]
    public void ByDefault_ADomainService_AddressedByItsName()
    {
        IServiceIdentity identity = new ServiceIdentity("widget-service", "Widget Service");

        Assert.Equal("widget-service", identity.Name);
        Assert.Equal("Widget Service", identity.Title);
        Assert.False(identity.IsProductService);
        Assert.Equal("widget-service", identity.Audience);
    }

    [Fact]
    public void AProductService_SaysSo()
    {
        IServiceIdentity identity = new ServiceIdentity("vault", "Vault", IsProductService: true);

        Assert.True(identity.IsProductService);
    }

    [Fact]
    public void Equality_IsByValue() =>
        Assert.Equal(new ServiceIdentity("vault", "Vault", true), new ServiceIdentity("vault", "Vault", true));
}
