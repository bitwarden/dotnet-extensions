namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// What an access token's <c>aud</c> names, and so which services accept it.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>
///     <description>
///     A service's token names the platform, <see cref="Platform"/>. Domain services accept this
///     audience and no other.
///     </description>
///   </item>
///   <item>
///     <description>
///     A person's token names the one product service it is for, by that service's
///     <see cref="IServiceIdentity.Name"/>. A product service accepts its own name and no other, so
///     a person's token leaked at one product service can't be used at another.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class TokenAudience
{
    /// <summary>
    /// The audience every service's token names.
    /// </summary>
    public const string Platform = "bitwarden";
}
