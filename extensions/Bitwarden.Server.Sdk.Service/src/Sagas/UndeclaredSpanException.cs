using Bitwarden.Server.Sdk.Sagas;

namespace Bitwarden.Server.Sdk.Service.Sagas;

/// <summary>
/// An operation's writes spanned services without a declaration of how it handles a failure part way
/// (<see cref="SpansServicesAttribute"/>), with <c>SpansServices:RefuseUndeclared</c> on.
/// </summary>
/// <remarks>
/// Answered as any unexpected failure is, a <c>500</c> describing nothing: it is a defect in the operation, never
/// something the caller did. The message says what to do.
/// </remarks>
/// <param name="message">What happened, naming the operation and where its writes went, and what to do.</param>
public sealed class UndeclaredSpanException(string message) : InvalidOperationException(message);
