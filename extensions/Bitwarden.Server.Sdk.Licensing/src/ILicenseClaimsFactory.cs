namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
///
/// </summary>
/// <typeparam name="T"></typeparam>
public interface ILicenseClaimsFactory<T>
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="context"></param>
    /// <param name="item"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask AddClaimsAsync(LicenseClaimsContext context, T item, CancellationToken cancellationToken);
}
