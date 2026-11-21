using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Authentication;

/// <summary>
/// Exposes the <c>Authentication</c> configuration section so that schemes can be configured through
/// <c>Authentication:Schemes:{SchemeName}</c>. This mirrors the framework provider that is only registered through
/// <c>AddAuthentication</c>, which is internal and so can't be registered directly.
/// </summary>
internal sealed class BitwardenAuthenticationConfigurationProvider : IAuthenticationConfigurationProvider
{
    private const string AuthenticationKey = "Authentication";

    private readonly IConfiguration _configuration;

    public BitwardenAuthenticationConfigurationProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IConfiguration AuthenticationConfiguration => _configuration.GetSection(AuthenticationKey);
}
