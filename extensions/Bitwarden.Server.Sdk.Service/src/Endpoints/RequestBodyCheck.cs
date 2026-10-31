using System.Reflection;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Reads a handler's plain JSON body before the framework does, so a body that cannot be read is
/// refused in the words an MVC controller uses rather than with the framework's empty <c>400</c>.
/// </summary>
/// <remarks>
/// <para>
/// The framework refuses an unreadable body before any endpoint filter runs, and says nothing
/// about why. So the endpoint's request delegate is wrapped, where a controller's model binding
/// would run: after authorization, before the body is read. A body that reads is handed on from
/// the buffer it was read into; one that doesn't is refused here, with everything else a
/// controller's model state would hold for the request.
/// </para>
/// <para>
/// In a controller's order: the platform's query types are bound first, so one that cannot be read
/// throws as it would while a controller binds it, and that answer wins. A body that is not JSON is
/// then a <c>415</c>; otherwise the route, query and header values that do not parse and the body's
/// own failure are refused together in one <c>422</c>.
/// </para>
/// </remarks>
internal static class RequestBodyCheck
{
    /// <summary>
    /// What a controller's JSON reader accepts, in the order its document lists them.
    /// </summary>
    internal static readonly string[] JsonMediaTypes = ["application/json", "text/json", "application/*+json"];

    /// <summary>
    /// The message a controller gives for an empty body an operation requires.
    /// </summary>
    internal const string EmptyBody = "A non-empty request body is required.";

    /// <summary>
    /// Wraps the endpoint's request delegate with what a controller does before binding: the
    /// operation's <see cref="IAuthorizesOperation"/> attributes, then, for a plain body, reading it.
    /// </summary>
    public static void Apply(EndpointBuilder endpoint, MethodInfo method, IServiceProvider services)
    {
        if (endpoint.RequestDelegate is not { } inner)
        {
            return;
        }

        var authorizations = (method.DeclaringType?.GetCustomAttributes(inherit: true) ?? [])
            .Concat(method.GetCustomAttributes(inherit: true))
            .OfType<IAuthorizesOperation>()
            .ToArray();
        var parameters = RequestParameters.For(method, services, new List<string>());
        var body = parameters.FirstOrDefault(parameter => parameter.Kind == ParameterKind.Body);
        var acceptsInvalid = AcceptsInvalid(method);

        if (body is not null && !endpoint.Metadata.OfType<IAcceptsMetadata>().Any(metadata => metadata is Attribute))
        {
            // Routing holds a request to no media type: the framework's own refusal there meets the
            // fallback policy before the operation's authorization, so an anonymous caller would be
            // told 401. A body that is not JSON is refused 415 below instead, after authorization,
            // where a controller refuses it. The document still lists what a controller's JSON reader
            // takes (StandardEndpointApiDescriptionProvider). An operation that declares its own media
            // types with [Consumes] keeps them, at routing, as a controller's do.
            foreach (var inferred in endpoint.Metadata.OfType<IAcceptsMetadata>().ToList())
            {
                endpoint.Metadata.Remove(inferred);
            }
        }

        if (authorizations.Length == 0 && body is null)
        {
            return;
        }

        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

        endpoint.RequestDelegate = async http =>
        {
            foreach (var authorization in authorizations)
            {
                await authorization.AuthorizeAsync(http);
            }

            if (body is not null)
            {
                if (await Check(http, body, options, acceptsInvalid) is { } failure)
                {
                    // A body that is not JSON is refused even where invalid requests are let through:
                    // a controller without [ApiController] cannot read it either, and says 415.
                    if (!acceptsInvalid || failure.StatusCode == StatusCodes.Status415UnsupportedMediaType)
                    {
                        await Refuse(http, parameters, failure).ExecuteAsync(http);
                        return;
                    }

                    // A controller without [ApiController] binds nothing and runs the action, which can
                    // still read the body as sent: kept for the handler (UnreadableRequestBody).
                    Keep(http);
                    Neutralize(http.Request);
                }
                else if (!http.Request.HasJsonContentType())
                {
                    // text/json, which a controller reads and the framework refuses.
                    http.Request.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;
                }
            }

            await inner(http);
        };
    }

    /// <summary>
    /// Whether the handler, or its class, lets an invalid request through.
    /// </summary>
    internal static bool AcceptsInvalid(MethodInfo method) =>
        method.IsDefined(typeof(AcceptsInvalidRequestsAttribute), inherit: true)
        || method.DeclaringType?.IsDefined(typeof(AcceptsInvalidRequestsAttribute), inherit: true) == true;

    /// <summary>
    /// Keeps the body the framework is about to be given in place of, for the handler.
    /// </summary>
    private static void Keep(HttpContext http)
    {
        if (http.Request.Body is MemoryStream buffer && buffer.Length > 0)
        {
            http.Features.Set(new UnreadableRequestBody(buffer.ToArray()));
        }
    }

    /// <summary>
    /// Gives the framework a body it reads as nothing.
    /// </summary>
    private static void Neutralize(HttpRequest request)
    {
        request.Body = new MemoryStream("null"u8.ToArray());
        request.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;
        request.ContentLength = 4;
    }

    private static async Task<BodyFailure?> Check(HttpContext http, RequestParameter body, JsonSerializerOptions options, bool acceptsInvalid)
    {
        var request = http.Request;

        // Read once, and handed on: the framework reads the same bytes from the buffer.
        var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, http.RequestAborted);
        buffer.Position = 0;
        request.Body = buffer;

        try
        {
            // No body and no media type is no body, to a controller without [ApiController]. One with it
            // has no reader for the body, and says 415, unless the body is optional: its binder then takes
            // a request that has no body as none (EmptyBodyBehavior.Allow), as an optional body is here.
            if ((acceptsInvalid || !body.Required) && buffer.Length == 0 && string.IsNullOrEmpty(request.ContentType))
            {
                return body.Required ? new BodyFailure(StatusCodes.Status422UnprocessableEntity, string.Empty, EmptyBody) : null;
            }

            if (!IsJson(request.ContentType))
            {
                // A controller has no reader for the body, so it cannot even say whether it is empty.
                return new BodyFailure(StatusCodes.Status415UnsupportedMediaType, string.Empty, string.Empty);
            }

            if (buffer.Length == 0)
            {
                return body.Required ? new BodyFailure(StatusCodes.Status422UnprocessableEntity, string.Empty, EmptyBody) : null;
            }

            // A body of null is no body, to a controller.
            return await JsonSerializer.DeserializeAsync(buffer, body.Info.ParameterType, options, http.RequestAborted) is null
                && body.Required
                    ? new BodyFailure(StatusCodes.Status422UnprocessableEntity, string.Empty, EmptyBody)
                    : null;
        }
        catch (JsonException unreadable)
        {
            return new BodyFailure(StatusCodes.Status422UnprocessableEntity, unreadable.Path ?? string.Empty, unreadable.Message);
        }
        finally
        {
            buffer.Position = 0;
        }
    }

    /// <summary>
    /// The answer to a request whose body cannot be read, as a controller would give it.
    /// </summary>
    private static IResult Refuse(HttpContext http, IReadOnlyList<RequestParameter> parameters, BodyFailure failure)
    {
        var errors = new List<ModelError>();
        var sources = new Dictionary<string, ErrorSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            switch (parameter.Kind)
            {
                case ParameterKind.Simple:
                    RequestRefusal.CheckSimple(http, parameter, errors, sources);
                    break;

                // Throws what binding would throw, which answers instead.
                case ParameterKind.Bound when RequestParameters.IsQueryType(parameter.Info.ParameterType):
                    if (RequestParameters.BindQueryType(parameter.Info, http.Request) is null && parameter.Required)
                    {
                        errors.Add(new ModelError(string.Empty, RequestRefusal.Required(parameter.DisplayName)));
                    }

                    break;

                case ParameterKind.Body when failure.StatusCode == StatusCodes.Status422UnprocessableEntity:
                    errors.Add(new ModelError(failure.Key, failure.Message));
                    break;
            }
        }

        // No body of its own, so the platform's status-code writer gives it the standard one.
        return failure.StatusCode == StatusCodes.Status415UnsupportedMediaType
            ? Results.StatusCode(StatusCodes.Status415UnsupportedMediaType)
            : RequestRefusal.Unprocessable(errors, sources);
    }

    /// <summary>
    /// The media types a controller's JSON reader accepts.
    /// </summary>
    private static bool IsJson(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        var type = parsed.MediaType.Value ?? string.Empty;

        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || type.Equals("text/json", StringComparison.OrdinalIgnoreCase)
            || (type.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                && type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }
}
