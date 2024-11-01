using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Extensions.Hosting.Licensing;

internal sealed class DefaultLicensingService : ILicensingService
{
    private readonly LicensingOptions _licensingOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultLicensingService> _logger;

    public DefaultLicensingService(
        IOptions<LicensingOptions> licensingOptions,
        TimeProvider timeProvider,
        ILogger<DefaultLicensingService> logger)
    {
        ArgumentNullException.ThrowIfNull(licensingOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        // TODO: Do we need to support runtime changes to these settings at all, I don't think we do...
        _licensingOptions = licensingOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;

        // We are cloud if the signing certificate has a private key that can sign licenses and local development
        // hasn't forced self host.
        IsCloud = _licensingOptions.SigningCertificate.HasPrivateKey && !_licensingOptions.ForceSelfHost;
    }

    public bool IsCloud { get; }

    public string CreateLicense(IEnumerable<Claim> claims, TimeSpan validFor)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(validFor, TimeSpan.Zero);

        if (!IsCloud)
        {
            throw new InvalidOperationException("Self-hosted services can not create a license, please check 'IsCloud' before calling this method.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;


        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            // Issuer = "bitwarden.com", // TODO: Is this what we want?
            // Audience = "configurable_product_name??", // TODO: Is this what we want?
            SigningCredentials = new SigningCredentials(
                new X509SecurityKey(_licensingOptions.SigningCertificate), SecurityAlgorithms.RsaSha256),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(validFor),
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    public async Task<IEnumerable<Claim>> VerifyLicenseAsync(string license)
    {
        ArgumentNullException.ThrowIfNull(license);
        // TODO: Should we validate that this is self host?
        // It's not technically wrong to be able to do that but we don't do it currently
        // so we could disallow it.

        var tokenHandler = new JwtSecurityTokenHandler();

        if (!tokenHandler.CanReadToken(license))
        {
            throw new InvalidLicenseException(InvalidLicenseReason.InvalidFormat);
        }

        var tokenValidateParameters = new TokenValidationParameters
        {
            IssuerSigningKey = new X509SecurityKey(_licensingOptions.SigningCertificate),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidateIssuer = false, // TODO: Do we want to have no issuer?
            ValidateAudience = false, // TODO: Do we want to have no audience?
#if DEBUG
            // It's useful to be stricter in tests so that we don't have to wait 5 minutes
            ClockSkew = TimeSpan.Zero,
#endif
        };

        var tokenValidationResult = await tokenHandler.ValidateTokenAsync(license, tokenValidateParameters);

        if (!tokenValidationResult.IsValid)
        {
            var exception = tokenValidationResult.Exception;
            _logger.LogWarning(exception, "The given license is not valid.");
            if (exception is SecurityTokenExpiredException securityTokenExpiredException)
            {
                throw new InvalidLicenseException(InvalidLicenseReason.Expired, null, securityTokenExpiredException);
            }
            else if (exception is SecurityTokenSignatureKeyNotFoundException securityTokenSignatureKeyNotFoundException)
            {
                throw new InvalidLicenseException(InvalidLicenseReason.WrongKey, null, securityTokenSignatureKeyNotFoundException);
            }
            // TODO: Handle other known failures

            throw new InvalidLicenseException(InvalidLicenseReason.Unknown, null, exception);
        }

        // Should I even take a ClaimsIdentity and return it here instead of a list of claims?
        return tokenValidationResult.ClaimsIdentity.Claims;
    }
}
