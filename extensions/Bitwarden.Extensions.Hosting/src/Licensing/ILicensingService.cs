using System.Security.Claims;

namespace Bitwarden.Extensions.Hosting.Licensing;

/// <summary>
/// A service with the ability to consume and create licenses.
/// </summary>
public interface ILicensingService
{
    /// <summary>
    /// Returns whether or not the current service is running as a cloud instance.
    /// </summary>
    bool IsCloud { get; }

    // TODO: Any other options than valid for?
    /// <summary>
    /// Creates a signed license that can be consumed on self-hosted instances.
    /// </summary>
    /// <remarks>
    /// This method can only be called when <see cref="IsCloud"/> returns <see langword="true" />.
    /// </remarks>
    /// <param name="claims">The claims to include in the license file.</param>
    /// <param name="validFor">How long the license should be valid for.</param>
    /// <exception cref="InvalidOperationException">
    /// The exception that is thrown if this method is called when the service is not running as a cloud service.
    /// </exception>
    /// <returns>
    /// A string representation of the license that can be given to people to store with their self hosted instance.
    /// </returns>
    string CreateLicense(IEnumerable<Claim> claims, TimeSpan validFor);

    /// <summary>
    /// Verifies that the given license is valid and can have it's contents be trusted.
    /// </summary>
    /// <param name="license">The license to check.</param>
    /// <exception cref="InvalidLicenseException">
    /// The exception that is thrown when the given license is invalid and data stored in it can not be trusted.
    /// </exception>
    /// <returns>An enumerable of claims included in the license.</returns>
    Task<IEnumerable<Claim>> VerifyLicenseAsync(string license);
}
