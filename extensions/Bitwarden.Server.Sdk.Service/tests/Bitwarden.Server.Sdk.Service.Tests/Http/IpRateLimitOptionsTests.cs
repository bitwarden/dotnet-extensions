using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// The per-address limits' settings: on, counting each endpoint apart and refusing with <c>429</c> unless configured
/// otherwise, read from the <c>IpRateLimitOptions</c> section in AspNetCoreRateLimit's shape.
/// </summary>
public sealed class IpRateLimitOptionsTests
{
    [Fact]
    public void EndpointRateLimiting_IsOnByDefault() =>
        Assert.True(new IpRateLimitOptions().EnableEndpointRateLimiting);

    [Fact]
    public void Defaults_OnWithNoRules_RefusingWith429()
    {
        var options = new IpRateLimitOptions();

        Assert.True(options.Enabled);
        Assert.Equal(429, options.HttpStatusCode);
        Assert.Empty(options.GeneralRules);
        Assert.Empty(options.IpWhitelist);
        Assert.Empty(options.EndpointWhitelist);
    }

    // A deployment's existing settings bind unchanged, under the section's name.
    [Fact]
    public void TheSection_BindsAsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IpRateLimitOptions:EnableEndpointRateLimiting"] = "false",
                ["IpRateLimitOptions:HttpStatusCode"] = "503",
                ["IpRateLimitOptions:IpWhitelist:0"] = "10.0.0.0/8",
                ["IpRateLimitOptions:EndpointWhitelist:0"] = "post:/accounts/register",
                ["IpRateLimitOptions:GeneralRules:0:Endpoint"] = "post:/connect/token",
                ["IpRateLimitOptions:GeneralRules:0:Period"] = "1m",
                ["IpRateLimitOptions:GeneralRules:0:Limit"] = "10",
                ["IpRateLimitOptions:GeneralRules:0:Grants:0"] = "password",
            })
            .Build();

        var options = configuration.GetSection(IpRateLimitOptions.SectionName).Get<IpRateLimitOptions>()!;

        Assert.Equal("IpRateLimitOptions", IpRateLimitOptions.SectionName);
        Assert.False(options.EnableEndpointRateLimiting);
        Assert.Equal(503, options.HttpStatusCode);
        Assert.Equal(["10.0.0.0/8"], options.IpWhitelist);
        Assert.Equal(["post:/accounts/register"], options.EndpointWhitelist);
        var rule = Assert.Single(options.GeneralRules);
        Assert.Equal(("post:/connect/token", "1m", 10), (rule.Endpoint, rule.Period, rule.Limit));
        Assert.Equal(["password"], rule.Grants);
    }
}
