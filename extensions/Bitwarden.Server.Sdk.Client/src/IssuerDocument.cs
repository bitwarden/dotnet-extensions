using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The one member of the authorization server's discovery document a <see cref="ClientAssertion"/> needs.
/// </summary>
/// <param name="Issuer">Its issuer identifier: the assertion's <c>aud</c>.</param>
internal sealed record IssuerDocument(
    [property: JsonPropertyName("issuer")] string? Issuer);
