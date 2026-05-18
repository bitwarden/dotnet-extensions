using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
///
/// </summary>
/// <typeparam name="T"></typeparam>
public interface ILicenseReader<T>
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="licenseContents"></param>
    /// <returns></returns>
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
