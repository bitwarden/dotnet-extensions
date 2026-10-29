namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What an access token's <c>aud</c> names, and so which services accept it.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of audience, because there are two kinds of caller:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>A service's token names the platform</b>, <see cref="Platform"/>. One audience because
///     there is one platform: a token narrowed to each callee would mean a token request per hop,
///     and a service-to-service call already authenticates the caller as itself. Domain services
///     accept this audience and no other.
///   </description></item>
///   <item><description>
///     <b>A person's token names the one product service it is for</b>, by that service's
///     registered name (<see cref="IServiceIdentity.Name"/>, for example
///     <c>password-manager-service</c>), exchanged from the person's session at Identity. A
///     product service accepts its own name and no other, so a person's token leaked or logged at
///     one product service can't be used at another (P06).
///   </description></item>
/// </list>
/// <para>
/// A person's session itself names Identity, so its only use is as the subject of that exchange.
/// </para>
/// </remarks>
public static class TokenAudience
{
    /// <summary>The audience every service's token names: the platform, not one service in it.</summary>
    public const string Platform = "bitwarden";
}
