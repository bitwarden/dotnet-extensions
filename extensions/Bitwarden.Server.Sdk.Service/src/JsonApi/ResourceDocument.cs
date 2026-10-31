using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A JSON:API document whose <c>data</c> is a single resource.
/// </summary>
/// <remarks>
/// Deliberately used for both directions: as the request body of a create or replace, and as the
/// response body of a read. One type means one schema in the OpenAPI document and one generated
/// class in the client, rather than a near-identical pair that drift.
/// <para>
/// Members that are only meaningful in one direction (<see cref="Meta"/>) are nullable and are
/// omitted from the wire when null, so a caller never sees a member the server did not populate.
/// </para>
/// </remarks>
/// <typeparam name="T">The resource type carried by this document.</typeparam>
public sealed record ResourceDocument<T> : ISingleResourceDocument, IEndpointParameterMetadataProvider where T : Resource
{
    /// <summary>
    /// The resource this document carries.
    /// </summary>
    public required T Data { get; init; }

    /// <summary>
    /// Non-standard information about the response that is not part of the resource itself.
    /// </summary>
    /// <remarks>
    /// Omitted entirely when unpopulated; a request never sets it.
    /// </remarks>
    [ReadOnly]
    public IDictionary<string, object?>? Meta { get; init; }

    /// <summary>
    /// The attributes a sparse fieldset asked for, or null for every one.
    /// </summary>
    /// <remarks>
    /// Not part of the document: the platform leaves the other attributes out when it writes it.
    /// Set from the operation's <see cref="Querying.Fields{T}"/>.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string>? Fields { get; init; }

    /// <summary>
    /// Where a created resource can be read, when it is not where the platform would derive.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? Location { get; init; }

    /// <summary>
    /// Lets the platform reach the resource without knowing <typeparamref name="T"/>.
    /// </summary>
    Resource ISingleResourceDocument.Resource => Data;

    /// <inheritdoc />
    string? ISingleResourceDocument.Location => Location;

    /// <summary>
    /// Binds a minimal-API handler's document from the request body, validating it in full first.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="parameter">The handler's parameter.</param>
    /// <returns>The document, or null when the body cannot be used.</returns>
    /// <remarks>
    /// A body that cannot be used binds nothing and leaves its failure for the standard filter,
    /// which refuses the request with it before the handler runs.
    /// </remarks>
    public static async ValueTask<ResourceDocument<T>?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;
        var (document, _) = await ResourceDocumentReading.ReadAsync(
            context, typeof(ResourceDocument<T>), typeof(T), options);

        return (ResourceDocument<T>?)document;
    }

    /// <summary>
    /// Documents the parameter as the operation's JSON request body.
    /// </summary>
    /// <param name="parameter">The handler's parameter.</param>
    /// <param name="builder">The endpoint being built.</param>
    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(new AcceptsMetadata([System.Net.Mime.MediaTypeNames.Application.Json], typeof(ResourceDocument<T>)));
    }
}
