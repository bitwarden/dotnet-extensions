namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What an access token's <c>aud</c> names, and so which services accept it.
/// </summary>
/// <remarks>
/// <para>
/// Every service accepts only tokens naming it, by its <see cref="IServiceIdentity.Name"/>, whoever
/// the caller is.
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     A service's token names the one service it calls. The caller asks Identity for a token per
///     callee, so a service token leaked at one service can't be used at another.
///     </description>
///   </item>
///   <item>
///     <description>
///     A person's token names the one product service it is for, exchanged from the person's session
///     at Identity, so a person's token leaked at one product service can't be used at another.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class TokenAudience
{
    /// <summary>
    /// The audience every service's token named before each named its callee. No service accepts it.
    /// </summary>
    public const string Platform = "bitwarden";
}
