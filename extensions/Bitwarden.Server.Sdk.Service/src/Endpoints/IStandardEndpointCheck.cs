namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// A rule a package holds the whole surface to once every endpoint is built: a declaration that
/// must be served by exactly one operation, say.
/// </summary>
/// <remarks>
/// The check at startup runs each one after every endpoint is built, so a rule can count what the
/// whole surface declared.
/// </remarks>
public interface IStandardEndpointCheck
{
    /// <summary>
    /// Adds what is wrong with the service's endpoints to <paramref name="problems"/>.
    /// </summary>
    /// <param name="endpoints">Every endpoint the service maps.</param>
    /// <param name="problems">Where problems are added, to stop the service starting.</param>
    void Check(IReadOnlyList<Microsoft.AspNetCore.Http.Endpoint> endpoints, ICollection<string> problems);
}
