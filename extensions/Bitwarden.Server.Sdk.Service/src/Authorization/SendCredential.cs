using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Reads a Send credential.
/// </summary>
public static class SendCredential
{
    /// <summary>
    /// The claim naming the Send a token was minted for.
    /// </summary>
    public const string Claim = "send_id";

    /// <summary>
    /// The Send the caller's token names, or null when it names none.
    /// </summary>
    public static string? SendId(ClaimsPrincipal caller) =>
        caller.FindFirst(Claim)?.Value is { Length: > 0 } send ? send : null;

    /// <summary>
    /// Whether the operation being called declares it accepts a Send credential.
    /// </summary>
    internal static bool DeclaredBy(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<AcceptsSendCredentialAttribute>() is not null;
}
