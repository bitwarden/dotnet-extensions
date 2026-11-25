namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// How many endpoint filters an endpoint was given besides the platform's own (<c>AddEndpointFilter</c> on it or its
/// group), counted once every convention has run: a check that must know whether anything stands between the request
/// and the handler reads it. The platform's own are the standard filter and the validation filter the framework adds
/// to every endpoint once a host calls <c>AddValidation</c>, where it has nothing of the service's own to check.
/// </summary>
/// <param name="Count">The filters, not counting the platform's.</param>
internal sealed record EndpointFilters(int Count);
