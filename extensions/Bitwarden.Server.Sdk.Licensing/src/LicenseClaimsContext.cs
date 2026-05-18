namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Accumulates the claims that will be embedded in a license under generation. Passed to each
/// registered <see cref="ILicenseClaimsFactory{T}"/> so it can contribute its claims.
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
    /// <param name="type">The claim type (e.g. <c>sub</c>, <c>user_id</c>).</param>
    /// <param name="value">The claim value.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a claim of <paramref name="type"/> has already been added to this context.
    /// </exception>
    public void AddClaim(string type, string value)
    {
        if (!_claims.TryAdd(type, value))
        {
            throw new InvalidOperationException($"A claim of type {type} has already been added.");
        }
    }

    /// <summary>
    /// Returns all claims that have been added to this context.
    /// </summary>
    /// <returns>A read-only view of the accumulated claims, keyed by claim type.</returns>
    public IReadOnlyDictionary<string, string> GetClaims() => _claims;
}
