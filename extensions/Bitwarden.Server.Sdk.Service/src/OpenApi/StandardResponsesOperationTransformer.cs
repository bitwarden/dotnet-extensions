using System.Reflection;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Gives every operation a single <c>default</c> failure response, and maps a void handler to 204.
/// </summary>
/// <remarks>
/// <para>
/// An operation declares only the success it can produce. Every failure (validation, not found,
/// forbidden, conflict, an unhandled fault) shares one shape, so it is described once as the
/// OpenAPI <c>default</c> response rather than enumerated status by status on every operation. A
/// generated client then has two branches: the declared success type, and
/// <see cref="ErrorResponse"/> for everything else.
/// </para>
/// <para>
/// Enumerating error statuses per operation is the alternative, and it decays: the list is
/// copy-pasted, drifts from what the handler actually does, and is never complete because any
/// operation can fail in ways its author did not enumerate. A <c>default</c> cannot be incomplete.
/// </para>
/// </remarks>
internal sealed class StandardResponsesOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Responses ??= new OpenApiResponses();

        // A handler that returns nothing succeeds with no content. The generator infers 200 from
        // the absence of a return value, which is the wrong success code for a delete.
        if (ReturnsNothing(context) && operation.Responses.Remove("200"))
        {
            operation.Responses["204"] = new OpenApiResponse
            {
                Description = "The request succeeded. There is no response body.",
            };
        }

        // The default failure response is attached at the document stage by
        // StandardErrorResponseDocumentTransformer, which can register ErrorResponse as a component
        // and reference it. Requesting the schema here yields an empty placeholder instead.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether the operation's handler returns no value.
    /// </summary>
    private static bool ReturnsNothing(OpenApiOperationTransformerContext context)
    {
        // An endpoint carries its handler's MethodInfo in metadata.
        var method = context.Description.ActionDescriptor.EndpointMetadata?.OfType<MethodInfo>().FirstOrDefault();

        return method is not null
            && (method.ReturnType == typeof(void)
                || method.ReturnType == typeof(Task)
                || method.ReturnType == typeof(ValueTask));
    }
}
