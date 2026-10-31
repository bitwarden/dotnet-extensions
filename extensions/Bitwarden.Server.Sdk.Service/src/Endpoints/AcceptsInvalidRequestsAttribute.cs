namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Lets a handler run for a request that failed binding or validation, as an action of an
/// ASP.NET MVC controller without <c>[ApiController]</c> does: the platform refuses nothing on the handler's
/// behalf.
/// </summary>
/// <remarks>
/// <para>
/// For the endpoints whose shape somebody else fixed and which answer a bad request in that
/// shape themselves: an existing API's routes served unchanged, a protocol's token endpoint. A body
/// that cannot be read binds as null, as it does on such a controller; declare the parameter
/// nullable. Its bytes stay in the request's <see cref="UnreadableRequestBody"/> feature, so the
/// handler can say why, as such a controller's model state does. A route, query or header value
/// the framework cannot parse still stops the request with a <c>400</c>, so declare one the
/// handler must see as it came as a string.
/// </para>
/// <para>
/// Everything else the standards do still applies: authorization, the operation identifier, the
/// documented response, the success status of a standard operation.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AcceptsInvalidRequestsAttribute : Attribute;
