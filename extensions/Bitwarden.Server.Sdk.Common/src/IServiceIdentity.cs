namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Which service this process is.
/// </summary>
public interface IServiceIdentity
{
    /// <summary>
    /// The service's identifier, for example <c>organization-service</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The service's human-readable name, for example <c>Organization Service</c>.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Whether this is a product service, which people call directly with their own session.
    /// </summary>
    /// <remarks>
    /// A product service takes the acting user from the session token and refuses a request that
    /// asserts one in <see cref="BitwardenHeaders.User"/>. A domain service is only called by other
    /// services and takes the acting user from the caller context they sign. Defaults to false.
    /// </remarks>
    bool IsProductService => false;

    /// <summary>
    /// The audience a token must name for this service to accept it: the service's own
    /// <see cref="Name"/>.
    /// </summary>
    string Audience => Name;
}
