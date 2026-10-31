namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Declares that a domain service's operation accepts a caller context a service asserted on its own authority
/// (<see cref="Common.CallerContextToken.Asserted"/>) naming a person: a call the system makes about somebody, as
/// background jobs, message consumers, administrative tools and seeders do.
/// </summary>
/// <remarks>
/// <para>
/// A domain service believes a call's context only as the context token Identity signed for it says. A
/// <see cref="Common.CallerContextToken.Delegated"/> token is chained to the person's own token, so it names only a
/// person whose request it is. An asserted one is the calling service's word: any holder of a service credential could
/// have one signed naming anybody. So an operation refuses an asserted context that names a person (a
/// <c>Bitwarden-User</c>) unless it carries this attribute, saying why the system calls it about somebody. An asserted
/// context naming nobody (the service's own call, about an organization or none) is accepted everywhere, as the
/// operation's scopes already bound it.
/// </para>
/// <para>
/// One per operation, on the handler method, naming the services that may call it so (their registered names, the
/// <c>internal.{name}</c> clients), so each is a reviewed, named exception, never a class's default. Any other service
/// asserting a person there is refused as everywhere else: a stolen credential asserts a person only where its own
/// service was let.
/// </para>
/// </remarks>
/// <param name="reason">Why those services call this operation about a person on their own authority.</param>
/// <param name="callers">The services that may: their registered names, such as <c>identity-service</c>.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AcceptsAssertedContextAttribute(string reason, params string[] callers) : Attribute
{
    /// <summary>
    /// Why those services call this operation about a person on their own authority.
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    /// The services that may: their registered names.
    /// </summary>
    public IReadOnlyList<string> Callers { get; } = callers;

    /// <summary>
    /// Whether the internal client <paramref name="client"/> (<c>internal.{name}</c>) is one of <see cref="Callers"/>.
    /// </summary>
    /// <param name="client">The calling token's <c>client_id</c>.</param>
    public bool Admits(string? client) =>
        client is not null
        && client.StartsWith(InternalCaller.ClientPrefix, StringComparison.Ordinal)
        && Callers.Contains(client[InternalCaller.ClientPrefix.Length..], StringComparer.Ordinal);
}
