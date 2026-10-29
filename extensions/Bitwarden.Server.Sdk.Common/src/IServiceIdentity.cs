namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Which service this process is.
/// </summary>
/// <remarks>
/// The service knows its own name from the moment it is constructed, but nothing it hosts did:
/// the name was a protected property of the base class. Anything that stamps work with where it
/// came from — an event saying which service published it — needs to ask, and a string copied
/// into configuration is a second place for the name to be wrong.
/// </remarks>
public interface IServiceIdentity
{
    /// <summary>The service's identifier, for example <c>organization-service</c>.</summary>
    string Name { get; }

    /// <summary>The service's human-readable name, for example <c>Organization Service</c>.</summary>
    string Title { get; }

    /// <summary>
    /// Whether this is a product service: people call it directly, holding their own session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// True for a product service — one marked <c>[ProductService]</c> — where a person's session
    /// ends: the acting user is the one the
    /// session token names, and a request asserting one in <see cref="BitwardenHeaders.User"/> is
    /// refused. False for a domain service, which is only ever called by other services and takes
    /// the acting user from that header.
    /// </para>
    /// <para>
    /// Here so that anything hosting or testing a service can tell which kind it is without being
    /// told twice. Defaulted, so an implementation predating it answers as a domain service.
    /// </para>
    /// </remarks>
    bool IsProductService => false;

    /// <summary>
    /// The audience a token must name for this service to accept it.
    /// </summary>
    /// <remarks>
    /// Follows from the two answers above, so a service names itself once: a product service
    /// accepts tokens addressed to it by <see cref="Name"/> (a person's token, exchanged for this
    /// product at Identity), and a domain service accepts tokens addressed to the platform,
    /// <see cref="TokenAudience.Platform"/> (another service's token). The hosting model validates
    /// it on every request; see <see cref="TokenAudience"/>.
    /// </remarks>
    string Audience => IsProductService ? Name : TokenAudience.Platform;
}
