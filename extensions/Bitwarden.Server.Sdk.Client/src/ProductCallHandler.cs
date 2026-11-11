using System.Net.Http.Headers;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Presents the token a <see cref="ProductClient"/>'s call was given, and keeps the service's answer as it came.
/// </summary>
/// <remarks>
/// A request that is part of no such call passes through untouched: the same named client may carry other requests,
/// with their own headers. The body is buffered before the generated client reads it, so the client reads it again
/// from memory and both see the same text.
/// </remarks>
internal sealed class ProductCallHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (ProductCall.Take() is not { } call)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", call.Bearer);

        var response = await base.SendAsync(request, cancellationToken);
        await response.Content.LoadIntoBufferAsync(cancellationToken);
        call.Record((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return response;
    }
}
