using System.Reflection;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Configures each endpoint a group's <see cref="StandardEndpointExtensions.WithStandards{TBuilder}"/>
/// maps: its OpenAPI metadata, success status and standard filter.
/// </summary>
internal static class StandardEndpointConvention
{
    public static void Apply(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.OfType<MethodInfo>().LastOrDefault() is not { } method)
        {
            return;
        }

        var services = endpoint.ApplicationServices;
        var problems = services.GetService<StandardEndpointProblems>();
        var name = NameOf(method);

        try
        {
            Configure(endpoint, method, name, services);
        }
        catch (InvalidOperationException refused) when (problems is not null)
        {
            problems.Add(refused.Message);
        }
    }

    /// <summary>
    /// How an operation is named in the messages that refuse it: its class and method.
    /// </summary>
    public static string NameOf(MethodInfo method) => $"{method.DeclaringType?.Name}.{method.Name}";

    private static void Configure(EndpointBuilder endpoint, MethodInfo method, string name, IServiceProvider services)
    {
        endpoint.Metadata.Add(StandardEndpointMetadata.Instance);

        // What the handler's class declares applies to every handler in it, as what an MVC
        // controller declares applies to its actions, and a handler's own declaration wins,
        // being later in the metadata.
        if (method.DeclaringType is { } declaring)
        {
            var inherited = declaring.GetCustomAttributes(inherit: true)
                .Where(attribute => attribute.GetType().Namespace != "System.Runtime.CompilerServices")
                .ToArray();

            for (var i = inherited.Length - 1; i >= 0; i--)
            {
                endpoint.Metadata.Insert(0, inherited[i]);
            }
        }

        var standard = method.GetCustomAttributes(inherit: true).OfType<IStandardOperation>().FirstOrDefault();
        var route = (endpoint as RouteEndpointBuilder)?.RoutePattern.RawText;

        if (standard is not null)
        {
            NameOperation(endpoint, method, route);
            DocumentResponse(endpoint, method, standard);
        }
        else
        {
            // Every answer is JSON, as MVC's [Produces("application/json")] makes it: a string the
            // framework would document as text is documented, and written, as JSON.
            foreach (var inferred in endpoint.Metadata.OfType<ProducesResponseTypeMetadata>()
                .Where(metadata => metadata.ContentTypes.Contains("text/plain")).ToList())
            {
                endpoint.Metadata.Remove(inferred);
                endpoint.Metadata.Add(new ProducesResponseTypeMetadata(
                    inferred.StatusCode, inferred.Type, [System.Net.Mime.MediaTypeNames.Application.Json]));
            }
        }

        Authorize(endpoint, method, standard, services);

        // A handler taking the person may only be reached with one, so binding it cannot fail.
        if (method.GetParameters().Any(parameter => parameter.ParameterType == typeof(Person))
            && OperationAuthorization.SettledIn(endpoint.Metadata) is not [{ User: ContextRequirement.Required, Anonymous: false }])
        {
            throw new InvalidOperationException(
                $"'{name}' takes the {nameof(Person)}, so it must require one: declare User = ContextRequirement.Required "
                + "on it or its class.");
        }

        if (method.GetCustomAttribute<AnyScopeAttribute>() is { } anyScope)
        {
            OperationAuthorization.ApplyAnyScope(name, anyScope, endpoint.Metadata);
        }

        var organization = method.IsDefined(typeof(NamesOrganizationAttribute), inherit: true);
        var provider = method.IsDefined(typeof(NamesProviderAttribute), inherit: true);

        // Inert below the product layer, which takes them from the service calling it.
        if ((organization || provider) && services.GetService<IServiceIdentity>()?.IsProductService == true)
        {
            OperationAuthorization.CheckNamesOwner(
                name,
                organization,
                provider,
                method.GetParameters().Select(parameter => (parameter.Name ?? string.Empty, parameter.ParameterType)),
                endpoint.Metadata);
        }

        if (method.IsDefined(typeof(AcceptsSendCredentialAttribute), inherit: true))
        {
            OperationAuthorization.CheckAcceptsSendCredential(name, endpoint.Metadata);
        }

        if (method.IsDefined(typeof(CreatesOrganizationAttribute), inherit: true))
        {
            OperationAuthorization.CheckCreatesOrganization(name, endpoint.Metadata);
        }

        // A form an API takes is not a browser's page posting back, and an MVC controller does not
        // ask it for an antiforgery token: the framework's own requirement on a form-bound endpoint
        // is waived, as a controller's [ApiController] waives it.
        if (method.GetParameters().Any(parameter => parameter.IsDefined(typeof(Microsoft.AspNetCore.Mvc.FromFormAttribute), inherit: true)))
        {
            endpoint.Metadata.Add(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute(required: false));
        }

        // Last, so it wraps the request delegate the framework built for the handler and runs
        // where an MVC controller's model binding would: after authorization, before the body is read.
        RequestBodyCheck.Apply(endpoint, method, services);
    }

    /// <summary>
    /// Gives the endpoint an operation identifier, unless it already declares one:
    /// <c>CreateUser</c> on <c>api/v2/users</c> is <c>createUserV2</c>, as on an MVC controller.
    /// </summary>
    private static void NameOperation(EndpointBuilder endpoint, MethodInfo method, string? route)
    {
        if (endpoint.Metadata.OfType<IEndpointNameMetadata>().Any())
        {
            return;
        }

        endpoint.Metadata.Add(new EndpointNameMetadata(
            StandardOperationShape.CamelCase(method.Name)
            + StandardOperationShape.VersionSuffix([route])));
    }

    /// <summary>
    /// Documents the success response from the handler's own return type, at the status the
    /// operation succeeds with, unless the handler declares a response of its own.
    /// </summary>
    /// <remarks>
    /// The framework infers a <c>200</c> from the return type. It is replaced, not added to, or
    /// a create would be documented as answering both.
    /// </remarks>
    private static void DocumentResponse(EndpointBuilder endpoint, MethodInfo method, IStandardOperation standard)
    {
        var responseType = ResponseType(method.ReturnType);
        var status = StandardOperationShape.SuccessStatus(standard, responseType);

        // An explicit declaration wins, as on an MVC controller: [ProducesResponseType] is MVC's
        // IApiResponseMetadataProvider, not the framework's IProducesResponseTypeMetadata.
        if (method.GetCustomAttributes(inherit: true)
            .Any(attribute => attribute is IProducesResponseTypeMetadata or Microsoft.AspNetCore.Mvc.ApiExplorer.IApiResponseMetadataProvider))
        {
            return;
        }

        foreach (var inferred in endpoint.Metadata.OfType<ProducesResponseTypeMetadata>().ToList())
        {
            endpoint.Metadata.Remove(inferred);
        }

        endpoint.Metadata.Add(responseType is null
            ? new ProducesResponseTypeMetadata(status, typeof(void))
            : new ProducesResponseTypeMetadata(status, responseType, [System.Net.Mime.MediaTypeNames.Application.Json]));
    }

    /// <summary>
    /// The type that reaches the wire, or null for a handler that returns no body.
    /// </summary>
    internal static Type? ResponseType(Type returnType)
    {
        var unwrapped = StandardOperationShape.UnwrapReturnType(returnType);

        // A result carries no type information a document could use.
        return unwrapped is not null && typeof(IResult).IsAssignableFrom(unwrapped) ? null : unwrapped;
    }

    /// <summary>
    /// Settles what the operation demands, from the service, its class and itself.
    /// </summary>
    private static void Authorize(
        EndpointBuilder endpoint,
        MethodInfo method,
        IStandardOperation? standard,
        IServiceProvider services)
    {
        var defaults = services.GetService<AuthorizationDefaults>() ?? new AuthorizationDefaults();

        var reads = standard?.IsRead
            ?? (endpoint.Metadata.OfType<IHttpMethodMetadata>().LastOrDefault()?.HttpMethods ?? [])
                .All(verb => string.Equals(verb, HttpMethods.Get, StringComparison.OrdinalIgnoreCase));

        OperationAuthorization.Apply(endpoint.Metadata, OperationAuthorization.Settle(
            method.GetCustomAttribute<AuthorizationAttribute>(inherit: true),
            method.DeclaringType?.GetCustomAttribute<AuthorizationAttribute>(inherit: true),
            defaults,
            reads));
    }
}
