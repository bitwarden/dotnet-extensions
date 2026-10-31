using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Says what an operation's request body is.
/// </summary>
/// <remarks>
/// <para>
/// .NET's XML comment support pairs an operation's <c>&lt;param&gt;</c> documentation with its
/// request body by position rather than by name, and the body is never the last parameter: a
/// handler takes a cancellation token after it. Every operation in every service therefore
/// described its request body as "Cancels the request."
/// </para>
/// <para>
/// Written by the platform rather than repaired, because the platform already knows what the
/// body is: the operation says whether it creates or replaces, and the parameter says which
/// resource. That is more accurate than prose an author would have to repeat on every handler,
/// and it cannot drift.
/// </para>
/// <para>
/// A document transformer rather than an operation one, and that is not incidental. The XML
/// comment support is registered by the service's own <c>AddOpenApi()</c> call, which runs
/// after the platform's, so an operation transformer here is overwritten by it. Document
/// transformers run after every operation transformer, whoever registered them.
/// </para>
/// </remarks>
internal sealed class RequestBodyDescriptionDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            if (!document.Paths.TryGetValue($"/{description.RelativePath?.TrimEnd('/')}", out var path)
                || path.Operations is null
                || description.HttpMethod is null
                || !path.Operations.TryGetValue(HttpMethod.Parse(description.HttpMethod), out var operation)
                || operation.RequestBody is null)
            {
                continue;
            }

            if (Describe(description, operation) is { } body)
            {
                operation.RequestBody.Description = body;
            }
        }

        return Task.CompletedTask;
    }

    private static string? Describe(
        Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description,
        OpenApiOperation operation)
    {
        var standard = description.ActionDescriptor.EndpointMetadata
            .OfType<IStandardOperation>()
            .FirstOrDefault();

        // An action's body is arguments, not a resource, so there is no shape for the platform
        // to describe. The payload type's own summary is the authored answer and is already in
        // the document, so it is lifted onto the body rather than restated.
        if (standard is StandardActionAttribute action)
        {
            var payload = operation.RequestBody?.Content?.Values
                .Select(media => media.Schema?.Description)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

            return payload ?? $"Arguments for the {action.Action} action.";
        }

        // The bulk operations and search say what they carry by their attribute alone; the
        // resource is the first type argument of whichever bulk body the handler takes.
        if (Bulk(description, standard) is { } bulk)
        {
            return bulk;
        }

        var resource = description.ActionDescriptor.Parameters
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ResourceDocument<>))
            .Select(type => ResourceTypes.For(type.GetGenericArguments()[0]))
            .FirstOrDefault();

        if (resource is null)
        {
            // Not a resource and not a standard operation: the author's own body type says what
            // it is, and that summary is lifted onto the body when .NET has paired it with the
            // wrong comment. An author's comment that was paired correctly is left alone.
            return standard is null && Mispaired(description)
                ? operation.RequestBody?.Content?.Values
                    .Select(media => media.Schema?.Description)
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "The request body."
                : null;
        }

        return standard?.SuccessStatusCode == StatusCodes.Status201Created
            ? $"The {resource} to create."
            : $"What the {resource} should become. An attribute left unset is cleared, not kept.";
    }

    private static string Targeted(Targeting targeting) => targeting switch
    {
        Targeting.Ids => "named by 'ids'",
        Targeting.Filter => "described by 'filter'",
        _ => "named by 'ids' or described by 'filter'",
    };

    private static bool TakesInput(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description) =>
        description.ActionDescriptor.Parameters.Any(parameter =>
            parameter.ParameterType.IsGenericType
            && parameter.ParameterType.GetGenericTypeDefinition() == typeof(BulkAction<,>));

    /// <summary>
    /// Whether .NET will have paired the body with the wrong comment: it pairs by position, so a
    /// body followed by a cancellation token gets the token's.
    /// </summary>
    private static bool Mispaired(Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description)
    {
        var parameters = description.ActionDescriptor.Parameters;
        var body = parameters.ToList().FindIndex(parameter =>
            parameter.BindingInfo?.BindingSource == Microsoft.AspNetCore.Mvc.ModelBinding.BindingSource.Body);

        return body >= 0
            && parameters.Skip(body + 1).Any(parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    private static readonly Type[] BulkBodies =
    [
        typeof(BulkCreate<>), typeof(BulkReplace<>), typeof(BulkUpdate<>), typeof(BulkDelete<>),
        typeof(BulkAction<>), typeof(BulkAction<,>), typeof(Search<>),
    ];

    private static string? Bulk(
        Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description,
        IStandardOperation? standard)
    {
        var resource = description.ActionDescriptor.Parameters
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsGenericType && BulkBodies.Contains(type.GetGenericTypeDefinition()))
            .Select(type => ResourceTypes.For(type.GetGenericArguments()[0]))
            .FirstOrDefault();

        if (resource is null)
        {
            return null;
        }

        return standard switch
        {
            StandardBulkCreateAttribute =>
                $"The {resource} resources to create, each as a single create takes it.",
            StandardBulkReplaceAttribute =>
                $"The {resource} resources to replace, each carrying its identifier. An attribute "
                + "left unset is cleared, not kept.",
            StandardBulkUpdateAttribute update =>
                $"The fields to change, and which {resource} resources to change, "
                + $"{Targeted(update.Targeting)}. A field left out is left alone.",
            StandardBulkDeleteAttribute delete =>
                $"Which {resource} resources to delete, {Targeted(delete.Targeting)}.",
            StandardBulkActionAttribute action when TakesInput(description) =>
                $"Which {resource} resources to {action.Action}, {Targeted(action.Targeting)}, and "
                + "what the action is given for them.",
            StandardBulkActionAttribute action =>
                $"Which {resource} resources to {action.Action}, {Targeted(action.Targeting)}.",
            StandardSearchAttribute =>
                $"The filter selecting which {resource} resources to return.",
            _ => null,
        };
    }
}
