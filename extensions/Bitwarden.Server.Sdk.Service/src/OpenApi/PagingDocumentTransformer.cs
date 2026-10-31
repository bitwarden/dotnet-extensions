using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Tidies the paging parameters an operation declares.
/// </summary>
/// <remarks>
/// <para>
/// The parameters themselves are the operation's own, declared in its signature with
/// <c>[FromQuery]</c>, so their names and their defaults reach the document without the
/// platform inventing anything. Two things about them still need correcting.
/// </para>
/// <para>
/// The schema, because .NET describes a query-bound integer as accepting either an integer or a
/// string matching a pattern. That is literally true of the binder and useless to a caller, and
/// it leaves nowhere for the bounds the standards put on a page.
/// </para>
/// <para>
/// The description, because the bracketed name a caller sends is not a C# identifier, and the
/// XML comment generator matches an author's <c>&lt;param&gt;</c> text to a parameter by its
/// published name. Nothing the author writes about these two can reach the document, so the
/// platform states what they mean, which is no loss: paging means the same thing in every
/// Bitwarden service, and forty rewordings of it would be worse than one.
/// </para>
/// </remarks>
internal sealed class PagingDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string Number = "page[number]";
    private const string Size = "page[size]";
    private const string After = "page[after]";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var operations = document.Paths?.Values
            .Where(path => path.Operations is not null)
            .SelectMany(path => path.Operations!.Values) ?? [];

        var declaredSizes = DeclaredSizes(document, context);

        foreach (var operation in operations)
        {
            if (operation.Parameters is null)
            {
                continue;
            }

            for (var i = 0; i < operation.Parameters.Count; i++)
            {
                operation.Parameters[i] = operation.Parameters[i].Name switch
                {
                    Number => Published(
                        operation.Parameters[i],
                        Number,
                        "Which page to return, from 1.",
                        minimum: 1,
                        maximum: null,
                        fallback: PageNumber.Default),

                    Size => Published(
                        operation.Parameters[i],
                        Size,
                        $"How many resources the page holds. Values above {Page.MaximumSize} are "
                        + $"treated as {Page.MaximumSize}.",
                        minimum: 1,
                        maximum: Page.MaximumSize,
                        fallback: declaredSizes.GetValueOrDefault(operation, Page.DefaultSize)),

                    After => new OpenApiParameter
                    {
                        Name = After,
                        In = ParameterLocation.Query,
                        Required = false,
                        Description = "Where the page starts: the cursor the previous page returned "
                            + "in meta.cursor, passed back unchanged. Omit it for the first page.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    },

                    _ => operation.Parameters[i],
                };
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The operations whose <see cref="PageSize"/> says <c>[DefaultValue]</c>, and what it says.
    /// </summary>
    /// <remarks>
    /// Read off the action, because the generator does not carry an attribute on a parameter of a
    /// type it does not describe as a number into the parameter's schema.
    /// </remarks>
    private static Dictionary<OpenApiOperation, int> DeclaredSizes(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context)
    {
        var sizes = new Dictionary<OpenApiOperation, int>();

        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            var declared = description.ActionDescriptor.Parameters
                .OfType<Microsoft.AspNetCore.Mvc.Infrastructure.IParameterInfoParameterDescriptor>()
                .Where(parameter => parameter.ParameterInfo.ParameterType == typeof(PageSize))
                .Select(parameter => parameter.ParameterInfo.GetCustomAttribute<DefaultValueAttribute>()?.Value)
                .OfType<int>()
                .Cast<int?>()
                .FirstOrDefault();

            if (declared is { } size
                && document.Paths is not null
                && document.Paths.TryGetValue($"/{description.RelativePath?.TrimEnd('/')}", out var path)
                && path.Operations is not null
                && description.HttpMethod is not null
                && path.Operations.TryGetValue(HttpMethod.Parse(description.HttpMethod), out var operation))
            {
                sizes[operation] = size;
            }
        }

        return sizes;
    }

    /// <remarks>
    /// The default is read back off the declared parameter rather than restated here, so the
    /// document keeps saying whatever the signature says: an <c>int</c> parameter's default, or a
    /// <see cref="PageSize"/>'s <c>[DefaultValue]</c>. A <see cref="PageNumber"/> or
    /// <see cref="PageSize"/> declared <c>= default</c> carries no value the document can read, so
    /// it gets the one the type stands for.
    /// </remarks>
    private static OpenApiParameter Published(
        IOpenApiParameter declared,
        string name,
        string description,
        int minimum,
        int? maximum,
        int fallback) =>
        new()
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = false,
            Description = description,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = minimum.ToString(),
                Maximum = maximum?.ToString(),
                Default = declared.Schema?.Default is JsonValue value && value.TryGetValue<int>(out _)
                    ? value.DeepClone()
                    : JsonValue.Create(fallback),
            },
        };
}
