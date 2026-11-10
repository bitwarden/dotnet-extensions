using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// The services a request has passed through on its way here, oldest first, as
/// <see cref="BitwardenHeaders.CallChain"/> carries them from one service to the next.
/// </summary>
/// <remarks>
/// <para>
/// So a service can tell it is being called back while one of its own requests is still waiting on the call: its own
/// name is already in the chain (a re-entry). A synchronous loop-back holds the first request open on work that may
/// need what that request holds, a lock or a transaction, and is invisible from either side alone.
/// </para>
/// <para>
/// Service names only (<see cref="IServiceIdentity.Name"/>), never an identifier of a person, an organization or any
/// data, and no more than <see cref="MaximumServices"/> of them: it is written to logs, spans and metric tags. A value
/// with anything but a service name in it is not a chain, and is read as none. A longer list of service names keeps
/// its most recent ones.
/// </para>
/// <para>
/// The current request's chain flows on the execution context (<see cref="Current"/>), as <see cref="ContextScope"/>
/// does, so every call the request makes extends it and nothing outside the request sees it.
/// </para>
/// </remarks>
public static class CallChain
{
    /// <summary>
    /// The most services a chain names. A longer one keeps its most recent services.
    /// </summary>
    public const int MaximumServices = 16;

    /// <summary>
    /// The longest service name a chain carries.
    /// </summary>
    public const int MaximumNameLength = 64;

    private const char Separator = ',';

    private static readonly AsyncLocal<Entered?> Ambient = new();

    /// <summary>
    /// The services the current request passed through before reaching this one, oldest first, or null when it came
    /// from no service (a person, an edge, a job, a message). A chain already left, by disposing one entered before
    /// it, is none.
    /// </summary>
    public static IReadOnlyList<string>? Current => Ambient.Value is { Ended: false } entered ? entered.Chain : null;

    /// <summary>
    /// Makes <paramref name="chain"/> the current request's until the returned value is disposed.
    /// </summary>
    /// <param name="chain">
    /// The chain the request arrived with, or null for none. A value that is not a chain is none, and a longer one keeps
    /// its most recent <see cref="MaximumServices"/>.
    /// </param>
    /// <returns>Restores whatever was current before, when disposed.</returns>
    public static IDisposable Enter(IReadOnlyList<string>? chain)
    {
        var entered = new Entered(IsChain(chain) ? [.. Recent(chain, MaximumServices)] : null, Ambient.Value);
        Ambient.Value = entered;

        return entered;
    }

    /// <summary>
    /// The chain an outgoing call carries: <paramref name="chain"/> with <paramref name="service"/> added last, its
    /// oldest services dropped beyond <see cref="MaximumServices"/>.
    /// </summary>
    /// <param name="chain">
    /// The chain the current request arrived with, if any. A value that is not a chain is none, so
    /// <paramref name="service"/> is carried alone.
    /// </param>
    /// <param name="service">The calling service's own name.</param>
    /// <exception cref="ArgumentException"><paramref name="service"/> is not a service name.</exception>
    public static IReadOnlyList<string> Append(IReadOnlyList<string>? chain, string service)
    {
        if (!IsServiceName(service))
        {
            throw new ArgumentException("The service's name is not a service name.", nameof(service));
        }

        if (!IsChain(chain))
        {
            return [service];
        }

        return [.. Recent(chain, MaximumServices - 1), service];
    }

    /// <summary>
    /// The header's value for <paramref name="chain"/>, or an empty string when it is not a chain. It doesn't drop
    /// services, so it can also name a chain and a service called back from it.
    /// </summary>
    /// <param name="chain">A chain, as <see cref="Append"/> or <see cref="TryParse"/> made it.</param>
    public static string Format(IReadOnlyList<string> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        return IsChain(chain) ? string.Join(Separator, chain) : string.Empty;
    }

    /// <summary>
    /// Reads the header's value: one to <see cref="MaximumServices"/> service names, comma-separated.
    /// </summary>
    /// <param name="value">The header's value.</param>
    /// <param name="chain">The services named, oldest first; null when the value is not a chain.</param>
    /// <returns>Whether the value is a chain.</returns>
    public static bool TryParse(string? value, out IReadOnlyList<string>? chain)
    {
        chain = null;

        if (string.IsNullOrEmpty(value) || value.Length > (MaximumServices * (MaximumNameLength + 1)))
        {
            return false;
        }

        var services = value.Split(Separator);

        if (services.Length > MaximumServices || !services.All(IsServiceName))
        {
            return false;
        }

        chain = services;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is shaped like a service's registered name: a lower-case letter, then lower-case
    /// letters, digits and single hyphens, no longer than <see cref="MaximumNameLength"/>, such as
    /// <c>organization-service</c>, and holding no identifier (a GUID written with or without its hyphens, which that
    /// shape would otherwise admit, alone or within a longer name). Nothing else can pass, so nothing else can reach a
    /// log or a metric tag.
    /// </summary>
    /// <param name="name">The candidate.</param>
    public static bool IsServiceName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaximumNameLength || name[0] is < 'a' or > 'z' || name[^1] == '-')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var character = name[i];

            if (character == '-' ? name[i - 1] == '-' : character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return !HoldsIdentifier(name);
    }

    private static bool IsChain([NotNullWhen(true)] IReadOnlyList<string>? chain) =>
        chain is { Count: > 0 } && chain.All(IsServiceName);

    private static IEnumerable<string> Recent(IReadOnlyList<string> chain, int count) =>
        chain.Skip(Math.Max(0, chain.Count - count));

    /// <summary>
    /// Whether a name already of a service name's shape holds a GUID: 32 hexadecimal digits in a row, or the
    /// hyphenated form.
    /// </summary>
    private static bool HoldsIdentifier(string name)
    {
        const int Digits = 32;
        const int Hyphenated = 36;

        var run = 0;

        foreach (var character in name)
        {
            run = char.IsAsciiHexDigitLower(character) ? run + 1 : 0;

            if (run == Digits)
            {
                return true;
            }
        }

        for (var start = 0; start + Hyphenated <= name.Length; start++)
        {
            if (Guid.TryParseExact(name.AsSpan(start, Hyphenated), "D", out _))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Entered(IReadOnlyList<string>? chain, Entered? outer) : IDisposable
    {
        public IReadOnlyList<string>? Chain { get; } = chain;

        public bool Ended { get; private set; }

        public void Dispose()
        {
            if (Ended)
            {
                return;
            }

            Ended = true;
            Ambient.Value = outer;
        }
    }
}
