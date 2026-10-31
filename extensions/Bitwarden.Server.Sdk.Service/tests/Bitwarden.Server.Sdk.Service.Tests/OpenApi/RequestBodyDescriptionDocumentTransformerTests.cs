using System.Reflection;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// An operation's request body is described from what the platform knows of it: the resource a
/// create or replace takes, what a bulk operation or search carries, an action's own arguments,
/// and, for anything else, the body type's summary where .NET would have given it the
/// cancellation token's.
/// </summary>
public sealed class RequestBodyDescriptionDocumentTransformerTests
{
    private const string Collection = "api/v1/gizmos";

    /// <summary>

    /// The resource the operations take.

    /// </summary>
    public sealed record Gizmo : Resource;

    /// <summary>

    /// What an action or a plain operation is given.

    /// </summary>
    public sealed record Arguments
    {
        /// <summary>
        /// Why.
        /// </summary>
        public string? Reason { get; init; }
    }

    [Theory]
    [InlineData(nameof(Handlers.CreateGizmo), "The gizmo to create.")]
    [InlineData(nameof(Handlers.ReplaceGizmo), "What the gizmo should become. An attribute left unset is cleared, not kept.")]
    [InlineData(nameof(Handlers.CreateGizmos), "The gizmo resources to create, each as a single create takes it.")]
    [InlineData(nameof(Handlers.ReplaceGizmos), "The gizmo resources to replace, each carrying its identifier. An attribute left unset is cleared, not kept.")]
    [InlineData(nameof(Handlers.UpdateGizmos), "The fields to change, and which gizmo resources to change, named by 'ids'. A field left out is left alone.")]
    [InlineData(nameof(Handlers.DeleteGizmos), "Which gizmo resources to delete, described by 'filter'.")]
    [InlineData(nameof(Handlers.Purge), "Which gizmo resources to purge, named by 'ids' or described by 'filter'.")]
    [InlineData(nameof(Handlers.Move), "Which gizmo resources to move, named by 'ids' or described by 'filter', and what the action is given for them.")]
    [InlineData(nameof(Handlers.SearchGizmos), "The filter selecting which gizmo resources to return.")]
    public async Task TransformAsync_StandardOperation_DescribesItsBody(string handler, string expected)
    {
        var body = await Transform(handler, payload: null);

        Assert.Equal(expected, body.Description);
    }

    [Fact]
    public async Task TransformAsync_Action_LiftsThePayloadsSummary()
    {
        var body = await Transform(nameof(Handlers.Archive), payload: "What archiving is given.");

        Assert.Equal("What archiving is given.", body.Description);
    }

    [Fact]
    public async Task TransformAsync_ActionWithoutASummary_NamesTheAction()
    {
        var body = await Transform(nameof(Handlers.Archive), payload: " ");

        Assert.Equal("Arguments for the archive action.", body.Description);
    }

    [Theory]
    [InlineData("The body's own summary.", "The body's own summary.")]
    [InlineData(null, "The request body.")]
    public async Task TransformAsync_PlainBodyBeforeACancellationToken_TakesTheBodysSummary(string? payload, string expected)
    {
        var body = await Transform(nameof(Handlers.Note), payload);

        Assert.Equal(expected, body.Description);
    }

    // .NET paired the author's comment with this body correctly, so it is left alone.
    [Fact]
    public async Task TransformAsync_PlainBodyLast_KeepsTheAuthorsComment()
    {
        var body = await Transform(nameof(Handlers.NoteLast), payload: "The body's own summary.");

        Assert.Equal("Cancels the request.", body.Description);
    }

    // A bulk body under an attribute that does not describe one says nothing of its own.
    [Fact]
    public async Task TransformAsync_BulkBodyUnderAnotherStandard_KeepsTheAuthorsComment()
    {
        var body = await Transform(nameof(Handlers.Mislabelled), payload: null);

        Assert.Equal("Cancels the request.", body.Description);
    }

    [Fact]
    public async Task TransformAsync_NoBody_IsSkipped()
    {
        var operation = new OpenApiOperation();

        await Run(Document(operation), nameof(Handlers.CreateGizmo), Collection);

        Assert.Null(operation.RequestBody);
    }

    [Fact]
    public async Task TransformAsync_OperationNotInTheDocument_IsSkipped()
    {
        var operation = Operation(payload: null);

        await Run(Document(operation), nameof(Handlers.CreateGizmo), "api/v1/elsewhere");

        Assert.Equal("Cancels the request.", operation.RequestBody!.Description);
    }

    [Fact]
    public async Task TransformAsync_NoPaths_IsLeftAlone()
    {
        var document = new OpenApiDocument { Paths = null! };

        await Run(document, nameof(Handlers.CreateGizmo), Collection);

        Assert.Null(document.Paths);
    }

    private static async Task<IOpenApiRequestBody> Transform(string handler, string? payload)
    {
        var operation = Operation(payload);
        await Run(Document(operation), handler, Collection);
        return operation.RequestBody!;
    }

    // As .NET leaves it: the body described with the cancellation token's comment.
    private static OpenApiOperation Operation(string? payload) => new()
    {
        RequestBody = new OpenApiRequestBody
        {
            Description = "Cancels the request.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = new OpenApiSchema { Description = payload } },
            },
        },
    };

    private static OpenApiDocument Document(OpenApiOperation operation) => new()
    {
        Paths = new OpenApiPaths
        {
            ["/" + Collection] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Post] = operation },
            },
        },
    };

    private static Task Run(OpenApiDocument document, string handler, string path)
    {
        var method = typeof(Handlers).GetMethod(handler)!;
        var description = new ApiDescription
        {
            HttpMethod = "POST",
            RelativePath = path,
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = [.. method.GetCustomAttributes(inherit: true), method],
                Parameters = [.. method.GetParameters().Select(ParameterDescriptor (parameter) => new HandlerParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterType = parameter.ParameterType,
                    ParameterInfo = parameter,
                    BindingInfo = BindingInfo.GetBindingInfo(parameter.GetCustomAttributes()),
                })],
            },
        };

        return new RequestBodyDescriptionDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [new ApiDescriptionGroup("v1", [description])],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One handler per kind of body.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// A create.
        /// </summary>
        [StandardCreate]
        public static void CreateGizmo(ResourceDocument<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A replace.

        /// </summary>
        [StandardUpdate]
        public static void ReplaceGizmo(string id, ResourceDocument<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk create.

        /// </summary>
        [StandardBulkCreate(Collection)]
        public static void CreateGizmos(BulkCreate<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk replace.

        /// </summary>
        [StandardBulkReplace(Collection)]
        public static void ReplaceGizmos(BulkReplace<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk update by identifiers.

        /// </summary>
        [StandardBulkUpdate(Collection, Targeting = Targeting.Ids)]
        public static void UpdateGizmos(BulkUpdate<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk delete by filter.

        /// </summary>
        [StandardBulkDelete(Collection, Targeting = Targeting.Filter)]
        public static void DeleteGizmos(BulkDelete<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk action without input.

        /// </summary>
        [StandardBulkAction(Collection)]
        public static void Purge(BulkAction<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk action with input.

        /// </summary>
        [StandardBulkAction(Collection)]
        public static void Move(BulkAction<Gizmo, Arguments> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A search.

        /// </summary>
        [StandardSearch(Collection)]
        public static void SearchGizmos(Search<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// A bulk body under a standard that does not describe one.

        /// </summary>
        [StandardReadOne]
        public static void Mislabelled(BulkCreate<Gizmo> request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// An action.

        /// </summary>
        [StandardAction]
        public static void Archive(string id, Arguments request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// Not a standard operation, its body before the token.

        /// </summary>
        public static void Note([FromBody] Arguments request, CancellationToken cancellationToken)
        {
        }

        /// <summary>

        /// Not a standard operation, its body last.

        /// </summary>
        public static void NoteLast(CancellationToken cancellationToken, [FromBody] Arguments request)
        {
        }
    }
}
