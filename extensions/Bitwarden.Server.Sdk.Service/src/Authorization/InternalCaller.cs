using System.Security.Claims;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Whether a caller is another service: it presented an internal service token, which Identity
/// issues only to a registered service (<c>client_id</c> <c>internal.{service}</c>).
/// </summary>
/// <remarks>
/// The one kind of caller whose caller-context headers a domain service believes.
/// Its context is what the call's context token says (<see cref="CallerContextHeaders"/>), signed by
/// Identity for this service and that caller: delegated from a person's own token where one is behind
/// the call, the calling service's own word otherwise.
/// </remarks>
public static class InternalCaller
{
    /// <summary>
    /// What an internal service token's <c>client_id</c> starts with.
    /// </summary>
    public const string ClientPrefix = "internal.";

    /// <summary>
    /// The claim naming the client a token was issued to.
    /// </summary>
    public const string ClientClaim = "client_id";

    /// <summary>
    /// Whether the validated token is an internal service's.
    /// </summary>
    /// <param name="caller">The request's principal.</param>
    public static bool Is(ClaimsPrincipal? caller) =>
        caller?.Identity?.IsAuthenticated == true
        && caller.FindFirst(ClientClaim)?.Value is { } client
        && client.StartsWith(ClientPrefix, StringComparison.Ordinal)
        && client.Length > ClientPrefix.Length;
}
