namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What one outgoing call asserts, as the plain <see cref="BitwardenHeaders"/> carry it.
/// </summary>
/// <param name="Organization">The organization, or null.</param>
/// <param name="User">The acting user, or null.</param>
/// <param name="Provider">The provider, or null.</param>
/// <param name="Context">The <see cref="BitwardenHeaders.Context"/> object, as written.</param>
public sealed record CallerContextAssertion(Guid? Organization, Guid? User, Guid? Provider, string Context);
