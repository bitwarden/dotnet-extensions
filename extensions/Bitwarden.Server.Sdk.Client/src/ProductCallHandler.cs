using System.Net.Http.Headers;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Presents the token a <see cref="ProductClient"/>'s call was given, and keeps the service's answer as it came.
/// </summary>
/// <remarks>
/// <para>
/// A request that is part of no such call passes through untouched: the same named client may carry other requests,
/// with their own headers. The body is buffered before the generated client reads it, so the client reads it again
/// from memory and both see the same text.
/// </para>
/// <para>
/// Safe to find twice in one pipeline: product clients registered over the same named client each add one. The
/// outermost takes the call and marks the request as its own, and any below it pass the request through. The same
/// request sent through the outermost again (by a retry above it) is a second request inside the call, and refused.
/// </para>
/// </remarks>
internal sealed class ProductCallHandler : DelegatingHandler
{
    /// <summary>
    /// The handler that took the call a request is part of.
    /// </summary>
    private static readonly HttpRequestOptionsKey<ProductCallHandler> TakenBy = new("Bitwarden.ProductCall.TakenBy");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if ((request.Options.TryGetValue(TakenBy, out var taker) && !ReferenceEquals(taker, this))
            || ProductCall.Take() is not { } call)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Options.Set(TakenBy, this);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", call.Bearer);

        var response = await base.SendAsync(request, cancellationToken);
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        call.Record((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return response;
    }
}
