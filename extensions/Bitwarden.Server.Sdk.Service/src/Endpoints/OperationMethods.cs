using System.Reflection;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Reads an operation's handler method from its descriptor.
/// </summary>
internal static class OperationMethods
{
    /// <summary>
    /// The handler method behind an operation.
    /// </summary>
    public static MethodInfo? Of(ActionDescriptor action) =>
        action.EndpointMetadata?.OfType<MethodInfo>().LastOrDefault();
}
