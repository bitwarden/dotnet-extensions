namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The body of a request to an <see cref="AcceptsInvalidRequestsAttribute"/> handler that the platform could not read
/// as the handler's parameter, as the client sent it: the handler's parameter binds as null, and a handler answering in a
/// shape somebody else fixed can still say why, as an MVC controller's model state does. In the request's features; absent
/// when the body was read, or there was none.
/// </summary>
/// <param name="Body">The bytes the client sent.</param>
public sealed record UnreadableRequestBody(ReadOnlyMemory<byte> Body);
