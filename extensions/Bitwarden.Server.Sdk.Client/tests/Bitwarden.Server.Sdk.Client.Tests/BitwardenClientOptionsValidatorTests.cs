namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A client that isn't configured stops the service at startup, naming every key that would have supplied what is
/// missing.
/// </summary>
public sealed class BitwardenClientOptionsValidatorTests
{
    [Fact]
    public void Validate_NothingConfigured_NamesEveryKeyConventionsReads()
    {
        var result = new BitwardenClientOptionsValidator("Widget-Service").Validate("Widget-Service", new BitwardenClientOptions());

        Assert.True(result.Failed);
        foreach (var key in new[]
        {
            "Services:Widget-Service:BaseAddress", "services:widget-service:https:0", "services:widget-service:http:0",
            "widget-service:baseUrl", "Services:Widget-Service:CallerName", "Bitwarden:ServiceName",
            "Services:Widget-Service:IdentityUri", "globalSettings:baseServiceUri:internalIdentity",
            "globalSettings:baseServiceUri:identity", "Authentication:Schemes:Bearer:Authority",
            "Services:Widget-Service:CallerSecret", "globalSettings:internalIdentityKey",
            "Services:Widget-Service:CallerKey", "Bitwarden:ClientKey",
        })
        {
            Assert.Contains(key, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Validate_EverythingConfigured_Succeeds()
    {
        var result = new BitwardenClientOptionsValidator("widget-service").Validate("widget-service", new BitwardenClientOptions
        {
            BaseAddress = "http://widget.invalid",
            CallerName = "gadget-service",
            IdentityUri = "http://identity.invalid",
            CallerSecret = "a-secret",
        });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0, 0.8, false)]
    [InlineData(-5, 0.8, false)]
    [InlineData(-1, 0.8, true)]
    [InlineData(30_000, 0, false)]
    [InlineData(30_000, 1, false)]
    [InlineData(30_000, double.NaN, false)]
    [InlineData(30_000, 0.8, true)]
    public void Validate_TimeoutAndRefreshAt_RefusedOutOfBounds(int timeoutMilliseconds, double refreshAt, bool valid)
    {
        var result = new BitwardenClientOptionsValidator("widget-service").Validate("widget-service", new BitwardenClientOptions
        {
            BaseAddress = "http://widget.invalid",
            CallerName = "gadget-service",
            IdentityUri = "http://identity.invalid",
            CallerSecret = "a-secret",
            Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds),
            RefreshAt = refreshAt,
        });

        Assert.Equal(valid, result.Succeeded);
    }
}
