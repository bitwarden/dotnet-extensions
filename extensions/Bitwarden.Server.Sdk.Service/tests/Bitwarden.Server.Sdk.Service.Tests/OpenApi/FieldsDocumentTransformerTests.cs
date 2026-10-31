using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A sparse fieldset is published once, as the optional <c>fields[type]</c> string a caller sends,
/// listing the attributes it may name, and the wrapper type's schema is not published.
/// </summary>
public sealed class FieldsDocumentTransformerTests
{
    /// <summary>
    /// A resource with one attribute of its own and one kept off the wire.
    /// </summary>
    public sealed record Gizmo : Resource
    {
        /// <summary>
        /// An attribute.
        /// </summary>
        public string? Label { get; init; }

        /// <summary>

        /// Not on the wire.

        /// </summary>
        [JsonIgnore]
        public string? Internal { get; init; }
    }

    [Fact]
    public async Task TransformAsync_Fields_IsPublishedUnderTheNameACallerSends()
    {
        var (document, operation) = Document(new OpenApiParameter { Name = "id" }, new OpenApiParameter { Name = "fields" });
        document.Components = new OpenApiComponents
        {
            Schemas = new Dictionary<string, IOpenApiSchema> { ["FieldsOfGizmo"] = new OpenApiSchema(), ["Gizmo"] = new OpenApiSchema() },
        };

        await Transform(document, nameof(Handlers.GetGizmo));

        Assert.Equal(["id", "fields[gizmo]"], operation.Parameters!.Select(parameter => parameter.Name));
        var fields = operation.Parameters![1];
        Assert.Equal(ParameterLocation.Query, fields.In);
        Assert.False(fields.Required);
        Assert.Equal(JsonSchemaType.String, fields.Schema!.Type);
        Assert.Equal(["Gizmo"], document.Components.Schemas.Keys);
    }

    [Fact]
    public async Task TransformAsync_Fields_ListsTheAttributesInOrder()
    {
        var (document, operation) = Document(new OpenApiParameter { Name = "fields" });

        await Transform(document, nameof(Handlers.GetGizmo));

        Assert.Equal(
            "A sparse fieldset: the attributes to return, comma separated. Every attribute when omitted. "
            + "One of: createdAt, createdBy, deletedAt, deletedBy, label, updatedAt, updatedBy, version.",
            Assert.Single(operation.Parameters!).Description);
    }

    // Run twice, as a document is generated more than once, the parameter is still published once.
    [Fact]
    public async Task TransformAsync_AlreadyPublished_IsNotRepeated()
    {
        var (document, operation) = Document(new OpenApiParameter { Name = "fields[gizmo]" });

        await Transform(document, nameof(Handlers.GetGizmo));

        Assert.Equal(["fields[gizmo]"], operation.Parameters!.Select(parameter => parameter.Name));
    }

    [Fact]
    public async Task TransformAsync_NoParameters_GetsTheFieldset()
    {
        var (document, operation) = Document();
        operation.Parameters = null;

        await Transform(document, nameof(Handlers.GetGizmo));

        Assert.Equal(["fields[gizmo]"], operation.Parameters!.Select(parameter => parameter.Name));
    }

    [Fact]
    public async Task TransformAsync_NoFieldset_IsLeftAlone()
    {
        var (document, operation) = Document(new OpenApiParameter { Name = "id" });

        await Transform(document, nameof(Handlers.DeleteGizmo));

        Assert.Equal(["id"], operation.Parameters!.Select(parameter => parameter.Name));
    }

    [Fact]
    public async Task TransformAsync_OperationNotInTheDocument_IsSkipped()
    {
        var (document, operation) = Document(new OpenApiParameter { Name = "fields" });

        await Transform(document, nameof(Handlers.GetGizmo), path: "api/v1/elsewhere/{id}");

        Assert.Equal(["fields"], operation.Parameters!.Select(parameter => parameter.Name));
    }

    private static (OpenApiDocument Document, OpenApiOperation Operation) Document(params OpenApiParameter[] parameters)
    {
        var operation = new OpenApiOperation { Parameters = [.. parameters] };
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/api/v1/gizmos/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
                },
            },
        };

        return (document, operation);
    }

    private static Task Transform(OpenApiDocument document, string handler, string path = "api/v1/gizmos/{id}")
    {
        var method = typeof(Handlers).GetMethod(handler)!;
        var description = new ApiDescription
        {
            HttpMethod = "GET",
            RelativePath = path,
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = [method],
                Parameters = [.. method.GetParameters().Select(ParameterDescriptor (parameter) => new HandlerParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterType = parameter.ParameterType,
                    ParameterInfo = parameter,
                })],
            },
        };

        return new FieldsDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [new ApiDescriptionGroup("v1", [description])],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A read taking a fieldset, and an operation that does not.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// Takes a fieldset.
        /// </summary>
        public static void GetGizmo(string id, Fields<Gizmo> fields)
        {
        }

        /// <summary>

        /// Takes none.

        /// </summary>
        public static void DeleteGizmo(string id)
        {
        }
    }
}
