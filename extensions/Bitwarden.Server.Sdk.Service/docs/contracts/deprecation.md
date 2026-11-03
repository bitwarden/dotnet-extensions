# Deprecation

The standard asks a deprecated API to say so twice: `deprecated: true` in the OpenAPI document with
a description naming the replacement, and `Deprecation` and `Sunset` headers, ideally with a `Link`
to the replacement, on every response. The package provides neither. It doesn't read
`[Obsolete]`, and adds no headers, so a service writes both itself: one attribute, one middleware
and one document transformer, shown below. The standard:
[Deprecation](https://contributing.bitwarden.com/architecture/server/apis/deprecation).

## What the package does

| The standard asks                            | The package                                                                                                                                                   |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `deprecated: true` in the OpenAPI document   | Not provided. `[Obsolete]` on a handler doesn't mark the operation deprecated, and with warnings as errors its `Map` line no longer compiles.                 |
| A `description` naming the replacement       | Not provided. The handler's `<remarks>` become the description; a transformer can prepend to it.                                                              |
| `Deprecation` and `Sunset` on every response | Not provided. `OnConfiguringPipeline` runs middleware outside the package's, so a service can add headers to every response, the package's refusals included. |
| A `Link` to the replacement                  | Not provided.                                                                                                                                                 |
| The replacement exists                       | A `v2` maps beside the `v1`, with its own operation identifiers. See [Breaking changes and versioning](breaking-changes-and-versioning.md).                   |

## Example

An attribute records when the operation was deprecated, when it stops working and what replaces
it:

```csharp
using System.Globalization;

/// <summary>
/// Marks an operation deprecated, with when it was, when it stops working, and what replaces it.
/// </summary>
/// <param name="deprecatedAt">When the operation became deprecated, as an ISO 8601 date and time.</param>
/// <param name="sunsetAt">When it stops working, as an ISO 8601 date and time.</param>
/// <param name="successor">
/// The path of the replacement, for the <c>Link</c> header. A route parameter such as <c>{id}</c> is
/// filled in from the request.
/// </param>
/// <param name="successorOperation">The replacement's operation identifier, for the OpenAPI description.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DeprecatedOperationAttribute(
    string deprecatedAt, string sunsetAt, string successor, string successorOperation) : Attribute
{
    public DateTimeOffset DeprecatedAt { get; } = DateTimeOffset.Parse(deprecatedAt, CultureInfo.InvariantCulture);
    public DateTimeOffset SunsetAt { get; } = DateTimeOffset.Parse(sunsetAt, CultureInfo.InvariantCulture);
    public string Successor { get; } = successor;
    public string SuccessorOperation { get; } = successorOperation;
}
```

The `v1` handler carries it, or the `v1` endpoints class does for every handler in it. Either way
the attribute is in the endpoint's metadata, where the middleware and the document find it:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/widgets").WithStandards().MapGet("{id}", GetWidget);

    /// <summary>
    /// Get a widget
    /// </summary>
    [DeprecatedOperation("2026-11-01T00:00:00Z", "2027-05-01T00:00:00Z", "/api/v2/widgets/{id}", "getWidgetV2")]
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));
}
```

Middleware adds the headers when the response starts. Registered in `OnConfiguringPipeline`, it
wraps the package's middleware, so a `401`, a `422` or a `404` from the package carries the headers
as a `200` does:

```csharp
using System.Globalization;

/// <summary>
/// Adds the <c>Deprecation</c>, <c>Sunset</c> and <c>Link</c> headers to every response from a
/// deprecated operation, refusals included.
/// </summary>
public sealed class DeprecationHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<DeprecatedOperationAttribute>() is { } deprecated)
        {
            // When the response starts, so the headers survive the platform's error handling.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Deprecation"] = $"@{deprecated.DeprecatedAt.ToUnixTimeSeconds()}";
                headers["Sunset"] = deprecated.SunsetAt.ToString("R", CultureInfo.InvariantCulture);
                headers.Append("Link", $"<{Fill(deprecated.Successor, context.Request.RouteValues)}>; rel=\"successor-version\"");
                return Task.CompletedTask;
            });
        }

        return next(context);
    }

    private static string Fill(string path, RouteValueDictionary values) =>
        values.Aggregate(path, (filled, value) =>
            filled.Replace($"{{{value.Key}}}", Uri.EscapeDataString(value.Value?.ToString() ?? string.Empty), StringComparison.Ordinal));
}
```

A document transformer marks the operation and names its replacement. It runs after the package's
transformers and the XML documentation's, so it prepends to the description they wrote:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Marks every deprecated operation <c>deprecated: true</c> in the OpenAPI document, and names its
/// replacement in its description.
/// </summary>
public sealed class DeprecationDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            if (description.ActionDescriptor.EndpointMetadata.OfType<DeprecatedOperationAttribute>().FirstOrDefault() is not { } deprecated
                || document.Paths.GetValueOrDefault($"/{description.RelativePath}") is not { Operations: { } operations }
                || operations.GetValueOrDefault(new HttpMethod(description.HttpMethod ?? "GET")) is not { } operation)
            {
                continue;
            }

            operation.Deprecated = true;
            operation.Description =
                $"Deprecated: call `{deprecated.SuccessorOperation}` instead. This operation stops working on "
                + $"{deprecated.SunsetAt.ToString("R", CultureInfo.InvariantCulture)}."
                + (string.IsNullOrEmpty(operation.Description) ? string.Empty : $"\n\n{operation.Description}");
        }

        return Task.CompletedTask;
    }
}
```

The service registers both, beside its `v1` and `v2` endpoints:

```csharp
using Bitwarden.Server.Sdk.Service;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddOpenApi(options => options.AddDocumentTransformer<DeprecationDocumentTransformer>());
        services.AddSingleton<IWidgetStore, WidgetStore>();
    }

    // Outside the platform's middleware, so a refusal (a 401, say) carries the headers too.
    protected override void OnConfiguringPipeline(WebApplication app) =>
        app.UseMiddleware<DeprecationHeaders>();

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        WidgetsEndpoints.Map(endpoints);
        WidgetsV2Endpoints.Map(endpoints);
    }
}
```

A caller of the `v1` operation sees:

```http
GET /api/v1/widgets/w1 HTTP/1.1
Authorization: Bearer ...
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Deprecation: @1793491200
Sunset: Sat, 01 May 2027 00:00:00 GMT
Link: </api/v2/widgets/w1>; rel="successor-version"

{"data":{"id":"w1","type":"widget","attributes":{"name":"Desk lamp"}}}
```

And the document publishes:

```json
"/api/v1/widgets/{id}": {
  "get": {
    "tags": ["Widgets"],
    "summary": "Get a widget",
    "description": "Deprecated: call `getWidgetV2` instead. This operation stops working on Sat, 01 May 2027 00:00:00 GMT.",
    "operationId": "getWidget",
    "deprecated": true
  }
}
```

The fragment leaves out the parameters and responses.

## What you decide

- **The dates.** `Sunset` must be a real date, and it must not pass without the operation being
  removed or a new date published. Nothing in the package watches it: put the date where your team
  will see it, and remove the `v1` endpoints class, or move the date, before it passes.
- **The replacement** each deprecated operation names, in its `Link` and its description.
- **One operation or a whole resource.** The package copies an endpoints class's attributes onto
  each of its standard endpoints, so the attribute on the `v1` endpoints class deprecates every
  operation in it, in the headers and in the document.
- **Telling callers.** The headers reach only callers that read them. Find who still calls a
  deprecated operation from your traces before its sunset.

## See also

- [Breaking changes and versioning](breaking-changes-and-versioning.md): publishing the replacement.
- [OpenAPI](openapi.md): document transformers and the committed contract.
- [Defining a service](../beyond-the-standards/defining-a-service.md): `OnConfiguringPipeline` and
  the middleware order.
- The standard:
  [Deprecation](https://contributing.bitwarden.com/architecture/server/apis/deprecation)
