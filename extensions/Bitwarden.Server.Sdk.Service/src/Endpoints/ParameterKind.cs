namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Where a handler's parameter comes from, as far as the standards are concerned.
/// </summary>
internal enum ParameterKind
{
    /// <summary>
    /// Supplied by the framework or the container: nothing a caller sends.
    /// </summary>
    Ignored,

    /// <summary>
    /// An SDK type that binds itself (<c>BindAsync</c>) and refuses what it cannot read.
    /// </summary>
    Bound,

    /// <summary>
    /// A resource document, which binds and checks itself and whose resource is validated.
    /// </summary>
    Document,

    /// <summary>
    /// A plain JSON body, read by the framework and validated by the platform.
    /// </summary>
    Body,

    /// <summary>
    /// A route, query or header value the framework parses.
    /// </summary>
    Simple,

    /// <summary>
    /// A model the framework binds member by member, from a form (<c>[FromForm]</c>) or from the
    /// query and route (<c>[AsParameters]</c>, what an MVC controller's complex <c>[FromQuery]</c> becomes),
    /// and the platform validates as a controller validates a model.
    /// </summary>
    Model,
}
