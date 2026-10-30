namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Facts about the acting person, read once in a request and remembered until it ends.
/// </summary>
/// <remarks>
/// <para>
/// A request often asks the same question many times, such as which collections a member's grants
/// reach. Each answer is the same for the rest of the request, so it is read once and remembered:
/// </para>
/// <code>
/// var grants = await memo.RememberAsync("grants", organization, member, () => ReadGrantsAsync(member));
/// </code>
/// <para>
/// A memo is scoped to one request, and every fact is also keyed by the organization and the
/// subject it is about. It is never shared across callers. Call <see cref="Forget"/> after a write
/// that could change a remembered fact.
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

    /// <summary>
    /// The fact, read the first time it is asked for in this request.
    /// </summary>
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

    /// <summary>
    /// Forgets everything remembered.
    /// </summary>
    public void Forget() => _facts.Clear();
}
