using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Facts about the acting person, read once in a request and remembered until it ends.
/// </summary>
/// <remarks>
/// <para>
/// A request often asks the same question many times: who the caller is in the organization, which
/// collections or projects their grants reach. Each answer is the same for the rest of the request,
/// so it is read once and remembered here:
/// </para>
/// <code>
/// var grants = await memo.RememberAsync("grants", organization, member, () => ReadGrantsAsync(member));
/// </code>
/// <para>
/// <b>Never across callers.</b> A memo is scoped to one request, which has one acting person, and
/// every fact is keyed by the organization and the person or membership it is about as well. The
/// only things it may be shared with are scopes opened for the same request (<see cref="Share"/>),
/// such as one per organization in a read across several. A cache that outlives the request is a
/// different design, with invalidation, and is not this.
/// </para>
/// <para>
/// <b>Never stale after a write.</b> Registered with
/// <c>AddRequestMemo&lt;TContext&gt;()</c> (Bitwarden.Server.Sdk.Data), it forgets everything whenever the
/// request saves to that database, so a request that changes a grant and then decides access reads
/// the grant again. A service with no database of its own registers it with
/// <see cref="RequestMemoServiceCollectionExtensions.AddRequestMemo"/>.
/// </para>
/// <para>
/// Not thread-safe: a request's work runs one step at a time.
/// </para>
/// </remarks>
public sealed class RequestMemo
{
    private Dictionary<(string Fact, Guid Organization, Guid Subject), object?> _facts = [];

    /// <summary>
    /// Shares the request's memo with a scope opened for the same request, so both remember the same
    /// facts.
    /// </summary>
    /// <param name="request">The memo of the request that opened this scope.</param>
    public void Share(RequestMemo request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _facts = request._facts;
    }

    /// <summary>The fact, read the first time it is asked for in this request.</summary>
    /// <typeparam name="T">The fact's type.</typeparam>
    /// <param name="fact">What is being asked: one name for each kind of fact.</param>
    /// <param name="organization">The organization it is about.</param>
    /// <param name="subject">The person or membership it is about.</param>
    /// <param name="read">Reads it.</param>
    /// <returns>The fact.</returns>
    public async Task<T> RememberAsync<T>(string fact, Guid organization, Guid subject, Func<Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentNullException.ThrowIfNull(read);

        var key = (fact, organization, subject);

        if (_facts.TryGetValue(key, out var known))
        {
            return (T)known!;
        }

        var answer = await read();
        _facts[key] = answer;

        return answer;
    }

    /// <summary>Forgets everything remembered.</summary>
    public void Forget() => _facts.Clear();
}

/// <summary>Registers <see cref="RequestMemo"/>.</summary>
public static class RequestMemoServiceCollectionExtensions
{
    /// <summary>
    /// A <see cref="RequestMemo"/> for each request, for a service with no database of its own to
    /// forget on.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddRequestMemo(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<RequestMemo>();

        return services;
    }
}
