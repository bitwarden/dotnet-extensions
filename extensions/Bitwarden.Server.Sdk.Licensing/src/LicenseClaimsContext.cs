namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
///
/// </summary>
public sealed class LicenseClaimsContext
{
    private readonly Dictionary<string, string> _claims;

    /// <summary>
    /// Creates a new instance of <see cref="LicenseClaimsContext"/>.
    /// </summary>
    public LicenseClaimsContext()
    {
        _claims = new Dictionary<string, string>();
    }

    /// <summary>
    /// Adds a claim.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="value"></param>
    public void AddClaim(string type, string value)
    {
        if (!_claims.TryAdd(type, value))
        {
            throw new InvalidOperationException($"A claim of type {type} has already been added.");
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <returns></returns>
    public IReadOnlyDictionary<string, string> GetClaims() => _claims;
}
