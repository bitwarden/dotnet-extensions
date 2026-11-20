using System.Net.Http.Headers;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Presents the calling service's credential on every outbound call.
/// </summary>
/// <param name="tokens">Supplies a current token.</param>
internal sealed class ServiceTokenHandler(IServiceTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ServiceFaultHandler.Obtaining(request, "a token");
        var token = await tokens.GetAsync(cancellationToken);
        ServiceFaultHandler.Obtained(request);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
