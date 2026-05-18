namespace Bitwarden.Server.Sdk.Licensing.Tests;

public class SigningCertificateProviderTests
{
    [Fact]
    public void TryGetFromCertificateStoreReturnsFalseWhenThumbprintNotFound()
    {
        var nonexistentThumbprint = new string('0', 40);

        var found = SigningCertificateProvider.TryGetFromCertificateStore(
            nonexistentThumbprint,
            password: null,
            out var certificate);

        Assert.False(found);
        Assert.Null(certificate);
    }
}
