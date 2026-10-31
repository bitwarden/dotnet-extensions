using Bitwarden.Server.Sdk.Service.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Holds a group's minimal-API endpoints to the platform's standards.
/// </summary>
/// <remarks>
/// <para>
/// One call per group, and the handlers keep their one attribute each:
/// </para>
/// <code>
/// public static void Map(IEndpointRouteBuilder endpoints)
/// {
///     var users = endpoints.MapGroup("api/v1/users").WithStandards();
///     users.MapPost("", CreateUser);
///     users.MapGet("{id}", GetUser);
/// }
///
/// [StandardCreate]
/// public static async Task&lt;ResourceDocument&lt;User&gt;&gt; CreateUser(
///     ResourceDocument&lt;User&gt; request, ICreateUserCommand createUser, CancellationToken cancellationToken)
///     => CreateResponse(await createUser.CreateAsync(request.Data, cancellationToken));
/// </code>
/// <para>
/// What the attribute would derive on an ASP.NET MVC controller action it derives here: the
/// operation identifier, the documented response, the status code, the settled authorization and
/// the checks on the declarations that depend on it. A request is bound and validated before the
/// handler runs, and refused in the words such a controller uses, so a caller sees the same
/// answers either way. The verb and route are restated in the <c>Map</c> line, and
/// the service refuses to start when they disagree with the attribute.
/// </para>
/// <para>
/// An <see cref="AuthorizationAttribute"/>, <c>[SelfHostedOnly]</c> or <c>[RequireFeature]</c> on
/// the class declaring the handlers applies to every handler in it.
/// </para>
/// </remarks>
public static class StandardEndpointExtensions
{
    /// <summary>
    /// Applies the standards to every endpoint the builder maps.
    /// </summary>
    /// <typeparam name="TBuilder">A group, or a single endpoint.</typeparam>
    /// <param name="builder">The group or endpoint.</param>
    /// <returns>The builder, for chaining.</returns>
    public static TBuilder WithStandards<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilterFactory(StandardEndpointFilter.Create);
        builder.Finally(StandardEndpointConvention.Apply);

        return builder;
    }
}
