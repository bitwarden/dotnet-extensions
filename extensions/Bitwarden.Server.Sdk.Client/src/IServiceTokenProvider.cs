namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Supplies the credential a service presents when calling another.
/// </summary>
/// <remarks>
/// An interface so that a caller can be exercised without an authorization server standing
/// behind it. A test of a client is a test of what it sends and how it reads the answer;
/// requiring Identity to be running to find that out would mean it mostly does not get tested.
/// </remarks>
public interface IServiceTokenProvider
{
    /// <summary>
    /// Returns a token to present.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<string> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a context token for one call to this provider's callee: the call's context,
    /// signed by Identity (<see cref="Bitwarden.Server.Sdk.Context.ContextToken"/>).
    /// </summary>
    /// <param name="assertion">What the call asserts, as its four headers carry it.</param>
    /// <param name="subjectToken">
    /// What proves whom the call is for (<see cref="Bitwarden.Server.Sdk.Context.ICallerProof"/>), or
    /// null when the call is the service's own, which then presents its own token.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The encoded token, or null when this provider issues none.</returns>
    /// <remarks>
    /// None by default, so a test's stand-in, which issues no context token, needs nothing more; a
    /// callee then sees the call carry none.
    /// </remarks>
    Task<string?> GetContextTokenAsync(
        Bitwarden.Server.Sdk.Context.ContextAssertion assertion,
        string? subjectToken,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
