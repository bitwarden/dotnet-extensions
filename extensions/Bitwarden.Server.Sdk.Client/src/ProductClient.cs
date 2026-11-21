using System.Net.Http.Headers;
using System.Text;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The base of a product service's client: every call made with a token the caller hands it, issued to somebody for
/// that service, and never with the calling service's own credential.
/// </summary>
/// <remarks>
/// <para>
/// A domain service's client presents its caller's credential (<c>AddBitwardenClient</c>). A product service serves
/// people: its token is a person's, exchanged for that service, or another principal's the service itself serves (a
/// machine account's, an access connector's). Its caller acts for the person in hand, so the token travels with each
/// call, not with the client, and two calls made at once for two people never
/// share one.
/// </para>
/// <para>
/// A derived client wraps its generated clients, constructed over <see cref="Http"/>, one method per operation, each
/// through <see cref="CallAsync{T}"/> or <see cref="CallAsync"/>. Where a caller relays a body or query of somebody
/// else's as it came, so that the service sees and refuses exactly what they sent, and the generated types couldn't
/// carry it (a value of the wrong type, text a typed parameter would reformat), the method sends it through
/// <see cref="SendAsync"/> instead, its path still the derived client's to own.
/// </para>
/// </remarks>
public abstract class ProductClient
{
    /// <summary>
    /// A client over <paramref name="http"/>, as its registration configures it.
    /// </summary>
    /// <param name="http">The service's address, timeout and handlers (<c>AddProductClient</c>).</param>
    protected ProductClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        Http = http;
    }

    /// <summary>
    /// The client the generated clients are constructed over.
    /// </summary>
    protected HttpClient Http { get; }

    /// <summary>
    /// Makes one generated call with <paramref name="bearer"/>, and keeps the answer as it came.
    /// </summary>
    /// <typeparam name="T">The generated type of the document the operation answers with.</typeparam>
    /// <param name="bearer">The token the call is made with. A credential: never logged.</param>
    /// <param name="call">The generated call; it must send exactly one request.</param>
    /// <returns>The answer, with the document when the generated client could read one.</returns>
    /// <exception cref="HttpRequestException">The service couldn't be reached.</exception>
    /// <exception cref="TaskCanceledException">The call took longer than the client allows, or was cancelled.</exception>
    protected async Task<ProductAnswer<T>> CallAsync<T>(string bearer, Func<Task<T>> call)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearer);
        ArgumentNullException.ThrowIfNull(call);

        var product = ProductCall.Begin(bearer);
        try
        {
            var document = await call();
            return new ProductAnswer<T>(product.AnsweredStatus, product.Body, document);
        }
        catch (Exception exception) when (product.Answered && exception is not OperationCanceledException)
        {
            // The generated client's refusal, or a body it couldn't read: the answer is what came.
            return new ProductAnswer<T>(product.AnsweredStatus, product.Body, null);
        }
        finally
        {
            product.End();
        }
    }

    /// <summary>
    /// Makes one generated call that answers with no document, with <paramref name="bearer"/>, and keeps the answer as
    /// it came.
    /// </summary>
    /// <param name="bearer">The token the call is made with. A credential: never logged.</param>
    /// <param name="call">The generated call; it must send exactly one request.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="HttpRequestException">The service couldn't be reached.</exception>
    /// <exception cref="TaskCanceledException">The call took longer than the client allows, or was cancelled.</exception>
    protected async Task<ProductAnswer> CallAsync(string bearer, Func<Task> call)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearer);
        ArgumentNullException.ThrowIfNull(call);

        var product = ProductCall.Begin(bearer);
        try
        {
            await call();
            return new ProductAnswer(product.AnsweredStatus, product.Body);
        }
        catch (Exception exception) when (product.Answered && exception is not OperationCanceledException)
        {
            return new ProductAnswer(product.AnsweredStatus, product.Body);
        }
        finally
        {
            product.End();
        }
    }

    /// <summary>
    /// Sends <paramref name="json"/> as it is to <paramref name="path"/>, with <paramref name="bearer"/>, and keeps the
    /// answer as it came: for a body or query relayed from somebody else's request, which the generated types couldn't
    /// carry unchanged.
    /// </summary>
    /// <param name="bearer">The token the call is made with. A credential: never logged.</param>
    /// <param name="method">The operation's method.</param>
    /// <param name="path">The operation's path, relative to the service's address, with any query.</param>
    /// <param name="json">The body, sent as <c>application/json</c> unread; null sends none.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="HttpRequestException">The service couldn't be reached.</exception>
    /// <exception cref="TaskCanceledException">The call took longer than the client allows, or was cancelled.</exception>
    protected async Task<ProductAnswer> SendAsync(
        string bearer,
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearer);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var product = ProductCall.Begin(bearer);
        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            return new ProductAnswer(product.AnsweredStatus, product.Body);
        }
        finally
        {
            product.End();
        }
    }
}
