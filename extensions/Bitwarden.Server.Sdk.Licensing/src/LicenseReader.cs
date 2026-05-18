using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Validates and reads the claims out of a license issued for items of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of item the license was issued for.</typeparam>
public interface ILicenseReader<T>
{
    /// <summary>
    /// Validates a license and returns its claims keyed by claim type.
    /// </summary>
    /// <param name="licenseContents">The serialized JWT license string.</param>
    /// <returns>The claims contained in the license.</returns>
    /// <exception cref="Microsoft.IdentityModel.Tokens.SecurityTokenException">
    /// Thrown when the license fails signature, issuer, audience, or lifetime validation.
    /// </exception>
    ValueTask<IReadOnlyDictionary<string, string>> ReadLicenseAsync(string licenseContents);
}

internal sealed class LicenseReader<T> : ILicenseReader<T>
{
    private static readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly IOptions<TokenValidationParameters> _tokenValidationParametersOptions;

    public LicenseReader(IOptions<TokenValidationParameters> tokenValidationParametersOptions)
    {
        _tokenValidationParametersOptions = tokenValidationParametersOptions;
    }

    public ValueTask<IReadOnlyDictionary<string, string>> ReadLicenseAsync(string licenseContents)
    {
        ArgumentException.ThrowIfNullOrEmpty(licenseContents);

        var tokenValidationParameters = _tokenValidationParametersOptions.Value;

        var claimsPrincipal = _tokenHandler.ValidateToken(licenseContents, tokenValidationParameters, out _);

        return new(claimsPrincipal.Claims.ToDictionary(c => c.Type, c => c.Value));
    }
}
