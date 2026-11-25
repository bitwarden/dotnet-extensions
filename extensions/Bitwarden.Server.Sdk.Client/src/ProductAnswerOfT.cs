namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A product service's answer to one call made with somebody's own token, with the document its contract says the call
/// returns, read by the generated client.
/// </summary>
/// <typeparam name="T">The generated type of the document the operation answers with.</typeparam>
/// <param name="Status">The HTTP status.</param>
/// <param name="Body">The body as text; empty when there was none.</param>
/// <param name="Document">
/// The document, read by the generated client; null when the service refused, or answered with something the contract
/// doesn't describe. <see cref="ProductAnswer.Body"/> is the answer either way.
/// </param>
/// <remarks>
/// Its <see cref="ProductAnswer.ToString"/> names the status alone, as every answer's does.
/// </remarks>
public sealed record ProductAnswer<T>(int Status, string Body, T? Document) : ProductAnswer(Status, Body)
    where T : class;
