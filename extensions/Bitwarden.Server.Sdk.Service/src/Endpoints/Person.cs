using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The person a request is made for, as a minimal-API handler's parameter.
/// </summary>
/// <remarks>
/// <para>
/// The equivalent of reading <see cref="IContext.UserId"/> in the handler and refusing when it is absent:
/// </para>
/// <code>
/// private Guid Person() => context.UserId ?? throw new ForbiddenException("The person is required.");
/// </code>
/// <para>
/// A handler takes it instead, and the platform settles it: a handler taking a
/// <see cref="Person"/> must require one (<c>User = ContextRequirement.Required</c>, declared or
/// inherited), and the service refuses to start otherwise. So authorization has already refused a
/// request naming nobody before this is bound, and the handler never sees one.
/// </para>
/// </remarks>
/// <param name="Id">The person's user identifier.</param>
public readonly record struct Person(Guid Id)
{
    /// <summary>
    /// Binds the person from the request's caller context.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The person.</returns>
    /// <exception cref="ForbiddenException">The request names nobody, which authorization should already have refused.</exception>
    public static ValueTask<Person> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.RequestServices.GetRequiredService<IContext>().UserId is { } user
            ? ValueTask.FromResult(new Person(user))
            : throw new ForbiddenException("The person is required.");
    }

    /// <summary>
    /// The person's user identifier.
    /// </summary>
    public static implicit operator Guid(Person person) => person.Id;
}
