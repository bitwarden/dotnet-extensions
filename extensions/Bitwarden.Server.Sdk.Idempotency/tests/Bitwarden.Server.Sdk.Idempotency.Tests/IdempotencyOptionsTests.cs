namespace Bitwarden.Server.Sdk.Idempotency.Tests;

public sealed class IdempotencyOptionsTests
{
    // The defaults are documented as the package's settings, so a service that sets nothing gets these.
    [Fact]
    public void Defaults_AreTheDocumentedOnes()
    {
        var options = new IdempotencyOptions();

        Assert.Equal(TimeSpan.FromHours(24), options.Retention);
        Assert.Equal(TimeSpan.FromMinutes(2), options.Lease);
        Assert.Equal(1024 * 1024, options.MaximumBytes);
        Assert.True(options.Enabled);
    }
}
