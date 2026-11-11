using System.Net;
using System.Text;
using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// Answers every outbound connection the service makes, and records it.
/// </summary>
internal sealed class RecordingHandler(Recorder recorder) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/connect/token", StringComparison.Ordinal))
        {
            var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&')
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            var exchange = form.GetValueOrDefault("requested_token_type") == ContextToken.TokenType;

            if (exchange)
            {
                form.Remove("client_secret");
                recorder.ContextExchanges.Enqueue(form);
            }
            else
            {
                recorder.TokenRequests.Enqueue(form.GetValueOrDefault("client_id"));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"{{(exchange ? SessionHost.IssuedContextToken : SessionHost.ServiceToken)}}","expires_in":300,"token_type":"Bearer"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        recorder.Downstream.Enqueue(request.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(',', header.Value),
            StringComparer.OrdinalIgnoreCase));

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}
