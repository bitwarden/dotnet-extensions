using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Describes a standard endpoint's operation as an MVC controller action's is described, so the
/// OpenAPI document is the one the same operations would publish as controller actions.
/// </summary>
/// <remarks>
/// <para>
/// The framework describes a minimal-API endpoint from its signature, but leaves out what it does
/// not bind itself: the platform's query types (<c>filter[…]</c>, <c>sort</c>, <c>page[…]</c>,
/// <c>fields[…]</c>) bind themselves, so are not there at all, and a resource document is named for
/// its type rather than its parameter. A controller's description has all of them, in the order the
/// action declares them, and the platform's document transformers read them from there.
/// </para>
/// <para>
/// So after the framework's provider has run, each standard endpoint's description gets the
/// parameters a controller action's would have, the request formats a controller's JSON reader
/// lists, and the handler's parameters as its action descriptor's.
/// </para>
/// </remarks>
/// <param name="services">To tell a dependency from a value, as the endpoint's own parameters were told apart.</param>
internal sealed class StandardEndpointApiDescriptionProvider(IServiceProvider services) : IApiDescriptionProvider
{
    /// <summary>
    /// After the framework's own providers, whose descriptions this completes.
    /// </summary>
    public int Order => 0;

    public void OnProvidersExecuting(ApiDescriptionProviderContext context)
    {
    }

    /// <summary>
    /// Describes the members the platform adds to a description, which the OpenAPI generator reads:
    /// a form field's container, and the validation attributes it publishes as a field's bounds.
    /// </summary>
    /// <remarks>
    /// MVC's own provider is not registered, because nothing binds through MVC. The members are
    /// described from their declarations alone, which is all the generator reads of them.
    /// </remarks>
    private static readonly ModelMetadataProvider Metadata = new DefaultModelMetadataProvider(new DeclaredValidation());

    /// <summary>
    /// A member's validation attributes, as the generator reads them from its metadata.
    /// </summary>
    private sealed class DeclaredValidation : ICompositeMetadataDetailsProvider
    {
        public void CreateBindingMetadata(BindingMetadataProviderContext context)
        {
        }

        public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
        {
        }

        public void CreateValidationMetadata(ValidationMetadataProviderContext context)
        {
            foreach (var attribute in context.Attributes.OfType<System.ComponentModel.DataAnnotations.ValidationAttribute>())
            {
                context.ValidationMetadata.ValidatorMetadata.Add(attribute);
            }
        }
    }

    public void OnProvidersExecuted(ApiDescriptionProviderContext context)
    {
        foreach (var description in context.Results)
        {
            var endpoint = description.ActionDescriptor.EndpointMetadata;

            if (endpoint is null
                || !endpoint.OfType<StandardEndpointMetadata>().Any()
                || endpoint.OfType<MethodInfo>().LastOrDefault() is not { } method)
            {
                continue;
            }

            Describe(description, method);
        }
    }

    private void Describe(ApiDescription description, MethodInfo method)
    {
        // A controller's description names a route value without its constraint ({id}, not
        // {id:guid}), and the platform's document transformers find operations by it.
        if (description.RelativePath is { } path && path.Contains(':', StringComparison.Ordinal))
        {
            description.RelativePath = RouteConstraint.Replace(path, "{${name}}");
        }

        // The operation's "controller" route value, and so its OpenAPI tag, is the handler's class,
        // named for its resource: an Endpoints suffix is dropped as MVC drops a controller's
        // Controller suffix, so GroupsEndpoints publishes under Groups.
        if (method.DeclaringType is { } declaring)
        {
            description.ActionDescriptor.RouteValues["controller"] = ControllerName(declaring);
            description.ActionDescriptor.RouteValues["action"] = method.Name;
        }

        var parameters = RequestParameters.For(method, services, new List<string>());
        var described = description.ParameterDescriptions.ToList();
        var ordered = new List<ApiParameterDescription>(described.Count);

        description.ActionDescriptor.Parameters = [.. method.GetParameters().Select(parameter => new HandlerParameterDescriptor
        {
            Name = parameter.Name ?? string.Empty,
            ParameterType = parameter.ParameterType,
            ParameterInfo = parameter,
            BindingInfo = BindingInfoFor(parameters.FirstOrDefault(each => each.Info == parameter)),
        })];

        foreach (var parameter in parameters)
        {
            var descriptor = description.ActionDescriptor.Parameters[parameter.Index];

            switch (parameter.Kind)
            {
                case ParameterKind.Simple:
                case ParameterKind.Body when description.SupportedRequestFormats.Count > 0:
                    if (described.FirstOrDefault(each => each.ParameterDescriptor?.Name == parameter.Info.Name) is { } existing)
                    {
                        existing.ParameterDescriptor = descriptor;
                        ordered.Add(existing);
                    }

                    break;

                // A body's media types are a controller's JSON reader's even where routing doesn't
                // hold a request to them ([AcceptsInvalidRequests]).
                case ParameterKind.Body when description.SupportedRequestFormats.Count == 0:
                    if (described.FirstOrDefault(each => each.ParameterDescriptor?.Name == parameter.Info.Name) is { } unrestricted
                        && !ordered.Contains(unrestricted))
                    {
                        unrestricted.ParameterDescriptor = descriptor;
                        ordered.Add(unrestricted);
                    }

                    foreach (var mediaType in RequestBodyCheck.JsonMediaTypes)
                    {
                        description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = mediaType });
                    }

                    break;

                // Described by the framework from the type's request metadata, under the type's name.
                case ParameterKind.Document:
                    if (described.FirstOrDefault(each => each.Source == BindingSource.Body) is { } body)
                    {
                        body.Name = parameter.Info.Name ?? body.Name;
                        body.ParameterDescriptor = descriptor;
                        body.ModelMetadata = Metadata.GetMetadataForParameter(parameter.Info);
                        ordered.Add(body);
                    }

                    break;

                // A form model, which a controller's explorer describes field by field, under the
                // names the fields are sent as; the framework describes it as one typed body.
                case ParameterKind.Model when parameter.Info.IsDefined(typeof(FromFormAttribute), inherit: true):
                    described.RemoveAll(each => each.Type == parameter.Info.ParameterType
                        || each.ParameterDescriptor?.Name == parameter.Info.Name);

                    foreach (var property in parameter.Underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(property => property.SetMethod is { IsPublic: true }))
                    {
                        ordered.Add(new ApiParameterDescription
                        {
                            Name = ModelValidation.BoundName(property),
                            Source = BindingSource.Form,
                            Type = property.PropertyType,
                            ParameterDescriptor = descriptor,
                            ModelMetadata = Metadata.GetMetadataForProperty(parameter.Underlying, property.Name),
                        });
                    }

                    description.SupportedRequestFormats.Clear();
                    foreach (var mediaType in description.ActionDescriptor.EndpointMetadata?.OfType<ConsumesAttribute>()
                        .SelectMany(consumes => consumes.ContentTypes).ToList() is { Count: > 0 } declared
                            ? declared
                            : ["application/x-www-form-urlencoded"])
                    {
                        description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = mediaType });
                    }

                    break;

                // Not described by the framework at all.
                case ParameterKind.Bound when RequestParameters.IsQueryType(parameter.Info.ParameterType):
                    ordered.Add(new ApiParameterDescription
                    {
                        Name = QueryName(parameter.Info.ParameterType) ?? parameter.Info.Name ?? string.Empty,
                        Source = BindingSource.Custom,
                        Type = parameter.Info.ParameterType,
                        IsRequired = false,
                        DefaultValue = DefaultOf(parameter.Info),
                        ParameterDescriptor = descriptor,
                        ModelMetadata = Metadata.GetMetadataForParameter(parameter.Info),
                    });
                    break;
            }
        }

        // Anything the framework described that is not one of the handler's parameters stays, after;
        // but not the body of no type the framework describes for a handler that declares what it
        // consumes ([Consumes]) and reads the raw request itself, which a controller's description
        // does not include.
        ordered.AddRange(described.Where(each => !ordered.Contains(each)
            && !(each.Type == typeof(void) && each.Source == BindingSource.Body)));

        description.ParameterDescriptions.Clear();
        foreach (var each in ordered)
        {
            description.ParameterDescriptions.Add(each);
        }

        if (parameters.Any(parameter => parameter.Kind == ParameterKind.Document))
        {
            description.SupportedRequestFormats.Clear();
            foreach (var mediaType in RequestBodyCheck.JsonMediaTypes)
            {
                description.SupportedRequestFormats.Add(new ApiRequestFormat { MediaType = mediaType });
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex RouteConstraint =
        new(@"\{(?<name>[^{}:]+):[^{}]*\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The query parameter a page parameter is read from, which its type names.
    /// </summary>
    /// <remarks>
    /// A filter, sort or fieldset is described under the handler's parameter name, as it always
    /// has been; the document transformers publish what each actually reads.
    /// </remarks>
    private static string? QueryName(Type type) =>
        type == typeof(Querying.PageNumber) ? Querying.PageNumber.Parameter
        : type == typeof(Querying.PageSize) ? Querying.PageSize.Parameter
        : type == typeof(Querying.PageAfter) ? Querying.PageAfter.Parameter
        : null;

    /// <summary>
    /// The name a handler's class publishes under: its own, without an <c>Endpoints</c> suffix.
    /// </summary>
    internal static string ControllerName(Type declaring) =>
        declaring.Name.EndsWith("Endpoints", StringComparison.Ordinal) && declaring.Name.Length > "Endpoints".Length
            ? declaring.Name[..^"Endpoints".Length]
            : declaring.Name;

    /// <summary>
    /// A parameter's default, as a controller's explorer gives it: a value type's own default when it declares <c>= default</c>.
    /// </summary>
    private static object? DefaultOf(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
        {
            return null;
        }

        var value = parameter.DefaultValue;

        // A non-nullable value type whose default reads as null or DBNull, as "= default" does: its own default.
        if (value is null or DBNull
            && parameter.ParameterType.IsValueType
            && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
        {
            return Activator.CreateInstance(parameter.ParameterType);
        }

        return value is DBNull ? null : value;
    }

    private static BindingInfo? BindingInfoFor(RequestParameter? parameter) => parameter?.Kind switch
    {
        ParameterKind.Body or ParameterKind.Document => new BindingInfo { BindingSource = BindingSource.Body },
        ParameterKind.Bound => new BindingInfo { BindingSource = BindingSource.Custom },
        ParameterKind.Simple => new BindingInfo
        {
            BindingSource = parameter.Place switch
            {
                ValuePlace.Header => BindingSource.Header,
                ValuePlace.Query => BindingSource.Query,
                ValuePlace.Route => BindingSource.Path,
                _ => null,
            },
            BinderModelName = parameter.ModelName == parameter.Info.Name ? null : parameter.ModelName,
        },
        ParameterKind.Ignored => new BindingInfo { BindingSource = BindingSource.Services },
        _ => null,
    };
}
