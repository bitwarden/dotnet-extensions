using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// With <c>Tls:RequireHttps</c>, the bearer handler's authority, where Identity's keys come from, must be https;
/// no authority, or another scheme's options, pass.
/// </summary>
public sealed class HttpsAuthorityTests
{
    [Theory]
    [InlineData("https://identity.internal", true)]
    [InlineData("HTTPS://identity.internal", true)]
    [InlineData(null, true)]
    [InlineData("http://identity.internal", false)]
    public void TheBearerAuthority_MustBeHttps(string? authority, bool valid)
    {
        var result = new InternalTlsTrust.HttpsAuthority().Validate(JwtBearerDefaults.AuthenticationScheme, new JwtBearerOptions { Authority = authority });

        Assert.Equal(valid, result.Succeeded);
    }

    // The failure names the setting and the authority, so whoever reads the startup error knows what to change.
    [Fact]
    public void APlainAuthority_FailsNamingTheSettingAndTheAuthority()
    {
        var result = new InternalTlsTrust.HttpsAuthority().Validate(JwtBearerDefaults.AuthenticationScheme, new JwtBearerOptions { Authority = "http://identity.internal" });

        Assert.Contains("RequireHttps", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("'http://identity.internal'", result.FailureMessage, StringComparison.Ordinal);
    }

    // Another scheme's options aren't the bearer handler's, and aren't checked.
    [Fact]
    public void AnotherScheme_IsNotChecked()
    {
        var result = new InternalTlsTrust.HttpsAuthority().Validate("other", new JwtBearerOptions { Authority = "http://identity.internal" });

        Assert.True(result.Succeeded);
    }
}
