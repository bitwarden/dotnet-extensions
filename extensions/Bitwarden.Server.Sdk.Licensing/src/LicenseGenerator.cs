using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Generates signed JWT licenses for items of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of item the license is issued for.</typeparam>
public interface ILicenseGenerator<T>
{
    /// <summary>
    /// Generates a signed license for the supplied item. Claims are contributed by each registered
    /// <see cref="ILicenseClaimsFactory{T}"/>.
    /// </summary>
    /// <param name="item">The item the license is being issued for.</param>
    /// <param name="expirationDate">The expiration of the license (the JWT <c>exp</c> claim).</param>
    /// <param name="cancellationToken">A token to cancel claim contribution.</param>
    /// <returns>The serialized JWT license string.</returns>
    public Task<string> GenerateAsync(T item, DateTimeOffset expirationDate, CancellationToken cancellationToken = default);
}

internal sealed class LicenseGenerator<T> : ILicenseGenerator<T>
{
    private static readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly IEnumerable<ILicenseClaimsFactory<T>> _claimsFactories;
    private readonly TimeProvider _timeProvider;
    private readonly StaticLicensingOptions _licensingOptions;
    private readonly SigningCredentials _signingCredentials;

    public LicenseGenerator(
        IEnumerable<ILicenseClaimsFactory<T>> claimsFactories,
        TimeProvider timeProvider,
        StaticLicensingOptions licensingOptions,
        ISigningCertificateProvider signingCertificateProvider,
        IHostEnvironment hostEnvironment)
    {
        if (!claimsFactories.Any())
        {
            throw new InvalidOperationException($"No Bitwarden.Server.Sdk.Licensing.ILicenseClaimsFactory<{typeof(T).FullName}> registered to contribute claims.");
        }

        _claimsFactories = claimsFactories;
        _timeProvider = timeProvider;

        var certificate = signingCertificateProvider.Get();
        var expectedThumbprint = licensingOptions.GetThumbprint(hostEnvironment.EnvironmentName);

        // TODO: We could delay these validation checks to when someone tries to generate the actual license.
        // The tradeoff is it allows you to use the 99% of the application that isn't generating licenses while improperly
        // configured but it can also hide an error. It also blocks the creation of new nodes if Azure blob storage
        // has an outage at the moment.
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("The configured signing certificate lacks a private key and is unable to sign licenses.");
        }

        if (certificate.Thumbprint != expectedThumbprint)
        {
            throw new InvalidOperationException($"The given certificate thumbprint does not match the expected thumbprint '{expectedThumbprint}' for environment: {hostEnvironment.EnvironmentName}");
        }

        _signingCredentials = new SigningCredentials(new X509SecurityKey(certificate), SecurityAlgorithms.RsaSha256);

        _licensingOptions = licensingOptions;
    }

    public async Task<string> GenerateAsync(T item, DateTimeOffset expirationDate, CancellationToken cancellationToken)
    {
        var context = new LicenseClaimsContext();

        foreach (var claimsFactory in _claimsFactories)
        {
            await claimsFactory.AddClaimsAsync(context, item, cancellationToken);
        }

        var now = _timeProvider.GetUtcNow();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(context.GetClaims().Select(p => new Claim(p.Key, p.Value))),
            Issuer = _licensingOptions.Issuer,
            Audience = _licensingOptions.Audience,
            SigningCredentials = _signingCredentials,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expirationDate.UtcDateTime,
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }
}
