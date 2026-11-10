namespace Bitwarden.Server.Sdk.Sagas;

/// <summary>
/// Says whether a service client's method sends a write, where its name says otherwise.
/// </summary>
/// <remarks>
/// <para>
/// A test host can stand in for another service with a double of its client, so no request is sent and the host
/// can't see a write by its method. It can go by the client method's name instead: a method named for reading
/// (<c>Get</c>, <c>List</c>, <c>Check</c>, <c>Find</c> and the like) reads, and any other writes. A method whose name
/// misleads says what it does here, on the client's interface.
/// </para>
/// <para>
/// Only for counting an operation's writes (<see cref="ServiceWrites"/>); the request the client sends is unchanged.
/// </para>
/// </remarks>
/// <param name="sends">Whether the method sends a write.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SendsWritesAttribute(bool sends = true) : Attribute
{
    /// <summary>
    /// Whether the method sends a write.
    /// </summary>
    public bool Sends { get; } = sends;
}
