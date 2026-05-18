namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Contributes claims to a license being generated for an item of type <typeparamref name="T"/>.
/// Multiple factories may be registered; each is invoked once per <see cref="ILicenseGenerator{T}.GenerateAsync"/>
/// call and may add claims to the shared <see cref="LicenseClaimsContext"/>.
/// </summary>
/// <typeparam name="T">The type of item the license is issued for.</typeparam>
public interface ILicenseClaimsFactory<T>
{
    /// <summary>
    /// Adds claims describing <paramref name="item"/> to <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The shared claims context being populated for the license under generation.</param>
    /// <param name="item">The item the license is being issued for.</param>
    /// <param name="cancellationToken">A token that may cancel the contribution.</param>
    /// <returns>A task that completes when all claims have been added.</returns>
    ValueTask AddClaimsAsync(LicenseClaimsContext context, T item, CancellationToken cancellationToken);
}
