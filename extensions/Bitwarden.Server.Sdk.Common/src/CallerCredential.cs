using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The kind of credential the request that started a chain of calls presented.
/// </summary>
/// <remarks>
/// Audit-only: no authorization, row filter or cache reads it. Written ALL_CAPS on the wire.
/// </remarks>
[JsonConverter(typeof(CallerCredentialJsonConverter))]
public enum CallerCredential
{
    /// <summary>
    /// A person's session.
    /// </summary>
    User = 1,

    /// <summary>
    /// An organization's API key, acting as the organization with nobody behind it.
    /// </summary>
    Organization,

    /// <summary>
    /// A Secrets Manager machine account's access token.
    /// </summary>
    Machine,

    /// <summary>
    /// The calling service itself, with no person acting.
    /// </summary>
    System,
}
