using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Licensing;

internal sealed class ConfigureTokenValidationParametersOptions : IConfigureOptions<TokenValidationParameters>
{
    private readonly IIssuerCertificateProvider _issuerCertificateProvider;
    private readonly StaticLicensingOptions _licensingOptions;

    public ConfigureTokenValidationParametersOptions(IIssuerCertificateProvider issuerCertificateProvider, StaticLicensingOptions licensingOptions)
    {
        ArgumentNullException.ThrowIfNull(issuerCertificateProvider);
        ArgumentNullException.ThrowIfNull(licensingOptions);

        _issuerCertificateProvider = issuerCertificateProvider;
        _licensingOptions = licensingOptions;
    }

    public void Configure(TokenValidationParameters options)
    {
        options.ValidAudience = _licensingOptions.Audience;
        options.ValidIssuer = _licensingOptions.Issuer;
        options.IssuerSigningKey = new X509SecurityKey(_issuerCertificateProvider.Get());
    }
}
