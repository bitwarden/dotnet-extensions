namespace Bitwarden.Server.Sdk.Licensing.Tests;

public class LicenseClaimsContextTests
{
    [Fact]
    public void ClaimTypeAddedTwice_Throws()
    {
        var context = new LicenseClaimsContext();
        context.AddClaim("test", "myclaim");

        var invalidOperationException = Assert.Throws<InvalidOperationException>(() => context.AddClaim("test", "otherclaim"));

        Assert.Contains("test", invalidOperationException.Message);
    }
}
