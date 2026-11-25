namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The organization, acting person and provider an outgoing call carries: what an <see cref="OutgoingCall"/> statement
/// names, and otherwise the context's.
/// </summary>
/// <param name="Organization">The organization the call is for, if any.</param>
/// <param name="User">The person acting, if any.</param>
/// <param name="Provider">The provider, if any.</param>
/// <param name="Asserted">
/// Whether this service asserts the context on its own authority rather than continuing a request's own (delegated)
/// context: a statement naming somebody else or making the call the system's, or work no request is behind, a message
/// handler's or a job's. The callee admits an asserted context naming a person only from the services an operation
/// names (<c>[AcceptsAssertedContext]</c>), and a read of the cache in the call's place only where it would.
/// </param>
public readonly record struct OutgoingCallContext(Guid? Organization, Guid? User, Guid? Provider, bool Asserted);
