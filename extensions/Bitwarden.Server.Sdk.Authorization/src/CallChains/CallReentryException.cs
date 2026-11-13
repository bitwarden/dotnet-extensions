namespace Bitwarden.Server.Sdk.Authorization.CallChains;

/// <summary>
/// A service was called back while a request of its own waited on the call, at an operation that doesn't accept it
/// (<see cref="AcceptsReentryAttribute"/>), with <c>CallChain:RefuseReentry</c> on.
/// </summary>
/// <remarks>
/// Answered as any unexpected failure is, a <c>500</c> describing nothing: it is a defect in how the services call each
/// other, never something the caller did. The log names the chain.
/// </remarks>
/// <param name="message">What happened, naming the operation and the chain.</param>
public sealed class CallReentryException(string message) : InvalidOperationException(message);
