using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Something a package adds to every standard endpoint it applies to, between the platform's
/// checks and the handler: the platform's read by identifier, say.
/// </summary>
/// <remarks>
/// The minimal-API counterpart of a package adding an action filter from an MVC convention.
/// Asked once per endpoint as it is built; one that does not apply returns null.
/// </remarks>
public interface IStandardEndpointFilterProvider
{
    /// <summary>
    /// The filter for a handler, or null when this does not apply to it.
    /// </summary>
    /// <param name="method">The handler.</param>
    /// <param name="problems">Where a declaration that cannot be honoured is reported, to stop the service starting.</param>
    /// <returns>The filter, or null.</returns>
    IEndpointFilter? For(MethodInfo method, ICollection<string> problems);
}
