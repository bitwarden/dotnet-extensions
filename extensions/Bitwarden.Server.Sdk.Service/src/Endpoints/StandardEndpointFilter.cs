using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// The endpoint filter every standard endpoint runs: what an MVC controller's model binding, model
/// validation, action filters and result filters do around an action.
/// </summary>
/// <remarks>
/// <para>
/// In order, as a controller has them:
/// </para>
/// <list type="number">
///   <item><description>
///     A resource document the platform could not use, then every binding and validation failure,
///     refused together with the status and the words a controller's model state would give.
///   </description></item>
///   <item><description>
///     The checks a controller's action filters make: the organization or provider a request
///     names, self-hosting, and a bulk operation's targeting and atomicity.
///   </description></item>
///   <item><description>What packages add (<see cref="IStandardEndpointFilterProvider"/>).</description></item>
///   <item><description>The handler, then its result: the operation's status code, a create's <c>Location</c>, a page's links.</description></item>
/// </list>
/// </remarks>
internal static class StandardEndpointFilter
{
    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext factory, EndpointFilterDelegate next)
    {
        var method = factory.MethodInfo;
        var services = factory.ApplicationServices;
        var problems = services.GetService<StandardEndpointProblems>();
        var found = new List<string>();

        var parameters = RequestParameters.For(method, services, found);
        var standard = method.GetCustomAttributes(inherit: true).OfType<IStandardOperation>().FirstOrDefault();
        var responseType = StandardEndpointConvention.ResponseType(method.ReturnType);
        var status = standard is null ? (int?)null : StandardOperationShape.SuccessStatus(standard, responseType);
        var declaring = method.DeclaringType;

        var selfHostedOnly = method.IsDefined(typeof(SelfHostedOnlyAttribute), true)
            || declaring?.IsDefined(typeof(SelfHostedOnlyAttribute), true) == true;
        var notSelfHostedOnly = method.IsDefined(typeof(NotSelfHostedOnlyAttribute), true)
            || declaring?.IsDefined(typeof(NotSelfHostedOnlyAttribute), true) == true;
        var namesOrganization = method.IsDefined(typeof(NamesOrganizationAttribute), true);
        var namesProvider = method.IsDefined(typeof(NamesProviderAttribute), true);
        (Atomicity Supports, Targeting Targeting)? bulk = BulkChecks.ChecksFilter(standard)
            ? BulkChecks.Declared([standard])
            : null;

        // What packages and the service add, then the filters its class and the handler carry as
        // attributes: what MVC's global, controller and action filters are to an action.
        var extensions = services.GetServices<IStandardEndpointFilterProvider>()
            .Select(provider => provider.For(method, found))
            .OfType<IEndpointFilter>()
            .Concat((declaring?.GetCustomAttributes(inherit: true) ?? []).OfType<IEndpointFilter>())
            .Concat(method.GetCustomAttributes(inherit: true).OfType<IEndpointFilter>())
            .ToArray();
        var acceptsInvalid = RequestBodyCheck.AcceptsInvalid(method);

        foreach (var problem in found)
        {
            if (problems is null)
            {
                throw new InvalidOperationException(problem);
            }

            problems.Add(problem);
        }

        // Innermost first, so the extensions run in registration order around the handler.
        var inner = next;
        for (var i = extensions.Length - 1; i >= 0; i--)
        {
            var extension = extensions[i];
            var then = inner;
            inner = context => extension.InvokeAsync(context, then);
        }

        return async context =>
        {
            var http = context.HttpContext;

            // A form model is bound by its fields' declared names, which the framework ignores, and
            // exists even when no field arrived, as a controller's always does.
            foreach (var parameter in parameters.Where(parameter => parameter.Kind == ParameterKind.Model))
            {
                if (parameter.Info.IsDefined(typeof(Microsoft.AspNetCore.Mvc.FromFormAttribute), inherit: true)
                    && context.Arguments.Count > parameter.Index)
                {
                    var model = context.Arguments[parameter.Index] ??= Activator.CreateInstance(parameter.Underlying);

                    if (model is not null)
                    {
                        RequestRefusal.FillFromForm(http, parameter, model);
                    }
                }
            }

            if (!acceptsInvalid && Refusal(context, parameters) is { } refused)
            {
                return refused;
            }

            if (namesOrganization || namesProvider)
            {
                NoteNamedOwner(context, namesOrganization, namesProvider);
            }

            if (selfHostedOnly || notSelfHostedOnly)
            {
                var selfHosted = http.RequestServices.GetRequiredService<IBitwardenEnvironment>().SelfHosted;

                if (selfHostedOnly && !selfHosted)
                {
                    throw new InvalidRequestException("Only allowed when self hosted.");
                }

                if (notSelfHostedOnly && selfHosted)
                {
                    throw new InvalidRequestException("Only allowed when not self hosted.");
                }
            }

            if (bulk is { } declared)
            {
                foreach (var argument in context.Arguments)
                {
                    BulkChecks.Check(argument, declared.Supports, declared.Targeting, http.Request.Query);
                }
            }

            var result = await inner(context);

            // Every answer is JSON, a string included, as MVC's [Produces("application/json")] made it.
            if (result is string text)
            {
                result = Results.Json(text);
            }

            if (status is { } succeeded)
            {
                return Succeeded(http, result, succeeded);
            }

            // Any other operation that returns nothing answers 204, unless it wrote its answer
            // itself.
            return result is null or EmptyHttpResult && !http.Response.HasStarted && http.Response.StatusCode == StatusCodes.Status200OK
                ? Results.NoContent()
                : result;
        };
    }

    /// <summary>
    /// The answer to a request that failed binding or validation, or null when it passed: what a
    /// controller's <c>[ApiController]</c> answers from its model state.
    /// </summary>
    private static object? Refusal(EndpointFilterInvocationContext context, IReadOnlyList<RequestParameter> parameters)
    {
        var http = context.HttpContext;

        // A resource document the platform rejected carries its own errors and its own status.
        if (http.Items.TryGetValue(ResourceDocumentReading.FailureKey, out var stored) && stored is DocumentFailure document)
        {
            return RequestRefusal.Errors(document.StatusCode, document.Errors);
        }

        // The framework marks a request it could not bind 400 and skips the handler, but still runs
        // this filter, which names what it refused.
        var bindingFailed = http.Response.StatusCode == StatusCodes.Status400BadRequest;
        var errors = new List<ModelError>();
        var sources = new Dictionary<string, ErrorSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            var argument = parameter.Index < context.Arguments.Count ? context.Arguments[parameter.Index] : null;

            switch (parameter.Kind)
            {
                // Always, not only when the framework refused something: the framework and a
                // controller's binder disagree at the edges (an empty value for a flag with a
                // default is the framework's default and a controller's refusal), and the
                // controller's answer is the one kept.
                case ParameterKind.Simple:
                    RequestRefusal.CheckSimple(http, parameter, errors, sources);
                    break;

                // Keyed by nothing, as a controller's model state keys a parameter its own binder
                // left unbound, so it points at the document.
                case ParameterKind.Bound when argument is null && parameter.Required:
                    errors.Add(new ModelError(string.Empty, RequestRefusal.Required(parameter.DisplayName)));
                    break;

                case ParameterKind.Document when argument is ISingleResourceDocument single:
                    if (single.Resource is { } resource)
                    {
                        ModelValidation.Validate(resource, nameof(ResourceDocument<Resource>.Data), http.RequestServices, errors);
                    }

                    break;

                case ParameterKind.Body when argument is null && parameter.Required:
                    errors.Add(new ModelError(parameter.ModelName, RequestRefusal.Required(parameter.DisplayName)));
                    break;

                case ParameterKind.Body when argument is not null:
                    ModelValidation.Validate(argument, string.Empty, http.RequestServices, errors);
                    break;

                case ParameterKind.Model:
                    RequestRefusal.CheckMembers(http, parameter, errors, sources);

                    if (argument is not null && !errors.Any())
                    {
                        ModelValidation.ValidateBound(argument, http.RequestServices, errors);
                    }

                    break;
            }
        }

        if (errors.Count > 0)
        {
            return RequestRefusal.Unprocessable(errors, sources);
        }

        if (bindingFailed)
        {
            // Refused by the framework for a reason this could not name. Never let through, and
            // never answered with an empty 400: the status-code writer gives it the standard body.
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        return null;
    }

    /// <summary>
    /// Notes the organization or provider a declared operation's body names, once it is bound, for
    /// <see cref="RequestNamedOwner"/>, so the named-owner check can authorize against it.
    /// </summary>
    private static void NoteNamedOwner(EndpointFilterInvocationContext context, bool organization, bool provider)
    {
        if (organization && context.Arguments.OfType<INamesOrganization>().FirstOrDefault() is { } named)
        {
            context.HttpContext.Items[RequestNamedOwner.OrganizationKey] = named.OrganizationId;
        }

        if (provider && context.Arguments.OfType<INamesProvider>().FirstOrDefault() is { } namedProvider)
        {
            context.HttpContext.Items[RequestNamedOwner.ProviderKey] = namedProvider.ProviderId;
        }
    }

    /// <summary>
    /// Applies the operation's success status to what the handler returned, and answers 204 when
    /// it returned nothing.
    /// </summary>
    internal static object? Succeeded(HttpContext http, object? result, int status)
    {
        switch (result)
        {
            // Nothing returned: the operation answers 204.
            case null or EmptyHttpResult:
                return Results.NoContent();

            // A result has chosen its own status, as an action's IActionResult has.
            case IResult:
                return result;
        }

        if (result is IDeferredLinks deferred)
        {
            result = deferred.WithLinks(http.Request);
        }

        // A handler that set the status itself has chosen it.
        if (http.Response.StatusCode == StatusCodes.Status200OK)
        {
            http.Response.StatusCode = status;
        }

        if (http.Response.StatusCode == StatusCodes.Status201Created)
        {
            if (result is ISingleResourceDocument { Location: { Length: > 0 } location })
            {
                http.Response.Headers.Location = location;
            }
            else if (!http.Response.Headers.ContainsKey(HeaderNames.Location)
                && result is ISingleResourceDocument { Resource.Id: { Length: > 0 } id })
            {
                http.Response.Headers.Location = $"{http.Request.PathBase.Add(http.Request.Path).Value?.TrimEnd('/')}/{id}";
            }
        }

        return result;
    }
}
