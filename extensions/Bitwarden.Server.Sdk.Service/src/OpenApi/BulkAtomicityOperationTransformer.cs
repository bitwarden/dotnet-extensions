using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Publishes the <c>atomic</c> parameter on every bulk operation.
/// </summary>
/// <remarks>
/// <para>
/// The platform honours <c>atomic</c> whether or not an operation declares it, so without this
/// it would be a parameter that works and is not written down, which a generated client
/// cannot send and a reader has no way to discover. The standards require every bulk API to
/// document the modes it supports and which is the default; this is that, taken from the
/// attribute rather than from prose an author has to remember to write.
/// </para>
/// <para>
/// An operation supporting one mode still publishes the parameter, with that mode as its only
/// permitted value. A caller reading the document then sees that asking for the other is
/// refused, rather than discovering it from a <c>422</c>.
/// </para>
/// </remarks>
internal sealed class BulkAtomicityOperationTransformer : IOpenApiOperationTransformer
{
    private const string Atomic = "atomic";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (Supported(context) is not { } supported)
        {
            return Task.CompletedTask;
        }

        var both = supported.HasFlag(Atomicity.AllOrNothing) && supported.HasFlag(Atomicity.BestEffort);
        var allOrNothing = supported.HasFlag(Atomicity.AllOrNothing);

        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Boolean,
            // The default is all-or-nothing wherever it is offered, which is the standards'
            // default too: half a change nobody asked for is worse than no change.
            Default = both || allOrNothing ? JsonValue.Create(true) : JsonValue.Create(false),
        };

        if (!both)
        {
            schema.Enum = [JsonValue.Create(allOrNothing)];
        }

        (operation.Parameters ??= []).Add(new OpenApiParameter
        {
            Name = Atomic,
            In = ParameterLocation.Query,
            Required = false,
            Description = both
                ? "Whether every resource must succeed for any to change. 'true' is "
                  + "all-or-nothing and the default; 'false' is best-effort, where each "
                  + "resource succeeds or fails on its own."
                : allOrNothing
                    ? "All-or-nothing: no resource changes unless every resource succeeds. This "
                      + "operation supports no other mode, and refuses 'false' with 422."
                    : "Best-effort: each resource succeeds or fails on its own. This operation "
                      + "supports no other mode, and refuses 'true' with 422.",
            Schema = schema,
        });

        return Task.CompletedTask;
    }

    /// <remarks>
    /// Read from whichever bulk attribute the operation carries. An operation carrying none is
    /// not a bulk operation and gets nothing.
    /// </remarks>
    private static Atomicity? Supported(OpenApiOperationTransformerContext context)
    {
        if (Endpoints.OperationMethods.Of(context.Description.ActionDescriptor) is not { } method)
        {
            return null;
        }

        var attributes = method.GetCustomAttributes(inherit: true);

        foreach (var each in attributes)
        {
            switch (each)
            {
                case StandardBulkActionAttribute bulkAction:
                    return bulkAction.Supports;
                case StandardBulkUpdateAttribute update:
                    return update.Supports;
                case StandardBulkDeleteAttribute delete:
                    return delete.Supports;
                case StandardBulkCreateAttribute create:
                    return create.Supports;
                case StandardBulkReplaceAttribute replace:
                    return replace.Supports;
            }
        }

        return null;
    }
}
