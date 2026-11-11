namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A product domain service is a domain service whose documentation is set apart; it can't also be
/// a product service.
/// </summary>
public sealed class ProductDomainServiceAttributeTests
{
    [Fact]
    public void Constructor_ProductDomainService_Accepted()
    {
        Assert.Null(Record.Exception(() => new ProductDomainProbe()));
    }

    [Fact]
    public void Constructor_MarkedBothKinds_Refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new BothKindsProbe());

        Assert.Contains("[ProductService] and [ProductDomainService]", error.Message);
    }

    [ProductDomainService]
    private sealed class ProductDomainProbe() : Service("product-domain-probe", "Product Domain Probe", []);

    [ProductService]
    [ProductDomainService]
    private sealed class BothKindsProbe() : Service("both-kinds-probe", "Both Kinds Probe", []);
}
