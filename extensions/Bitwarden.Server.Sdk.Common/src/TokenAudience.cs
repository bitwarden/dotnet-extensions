namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What an access token's <c>aud</c> names, and so which services accept it.
/// </summary>
/// <remarks>
/// <para>
/// A host accepts only tokens naming its service, by <see cref="IServiceIdentity.Audience"/>,
/// whoever the caller is.
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     A service's token names the one service it calls, so a service token leaked at one service
///     can't be used at another.
///     </description>
///   </item>
///   <item>
///     <description>
///     A person's token names the one product service it is for, so a person's token leaked at one
///     product service can't be used at another.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class TokenAudience
{
    /// <summary>
    /// A retired audience shared by every service. A host never accepts it.
    /// </summary>
    public const string Platform = "bitwarden";
}
