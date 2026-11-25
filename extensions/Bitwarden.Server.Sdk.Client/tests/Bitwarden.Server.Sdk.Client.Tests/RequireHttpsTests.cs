using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// Tls:RequireHttps: a client with a plain service or identity address stops the service at startup.
/// </summary>
public sealed class RequireHttpsTests
{
    [Theory]
    [InlineData("https://callee.internal", "https://identity.internal", true, true)]
    [InlineData("http://callee.internal", "https://identity.internal", true, false)]
    [InlineData("https://callee.internal", "http://identity.internal", true, false)]
    [InlineData("http://callee.internal", "http://identity.internal", false, true)]
    public void Validate_PlainAddresses_RefusedOnlyWhenRequired(string baseAddress, string identity, bool required, bool valid)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:callee-service:BaseAddress"] = baseAddress,
                ["Services:callee-service:IdentityUri"] = identity,
                ["Services:callee-service:CallerName"] = "caller",
                ["Services:callee-service:CallerSecret"] = "made-up",
                ["Tls:RequireHttps"] = required ? "true" : null,
            })
            .Build();
        var options = new BitwardenClientOptions();
        Conventions.Apply(options, configuration, "callee-service");

        var result = new BitwardenClientOptionsValidator("callee-service").Validate("callee-service", options);

        Assert.Equal(required, options.RequireHttps);
        Assert.Equal(valid, result.Succeeded);
    }
}
