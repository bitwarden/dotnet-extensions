namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Declares that a product service's operation names, in its path or its body, a provider the
/// person's session was not issued for (accepting an invitation to its staff), and carries it on its
/// downstream calls. As <see cref="NamesOrganizationAttribute"/>, on the provider's axis: the path's
/// <c>{providerId}</c>, or a body implementing <see cref="INamesProvider"/>; the session must name no
/// provider (<c>Provider = Forbidden</c>) and a person, and no role is demanded.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NamesProviderAttribute : Attribute
{
    /// <summary>
    /// The route value an operation names the provider in.
    /// </summary>
    public const string RouteValue = "providerId";
}
