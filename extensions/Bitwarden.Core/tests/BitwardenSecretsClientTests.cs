using System.Net.Http.Json;

namespace Bitwarden.Core.Tests;

public class BitwardenSecretsClientTests
{
    const string TestSecretId = "a0ec36c3-e153-4c3b-b38d-a858a38b8d6e";
    const string TestEncryptedString = "2.pDHeLbEbD3jWDmnFqYwI7g==|FAN55mPW4MZL+P9c4VkqIRDoAXdcHqHv4KpO50bwcvY=|yFJuxPEJD3oOqZ0v8U+WSKIX5Kr+/d0sCPi8Jwxb+ek=";

    // https://github.com/bitwarden/dotnet-extensions/issues/49
    [Theory]
    [InlineData("http://localhost:33656", "http://localhost:4000", "http://localhost:33656/connect/token", $"http://localhost:4000/secrets/{TestSecretId}")] // Dev environment
    [InlineData("http://localhost:33656/", "http://localhost:4000/", "http://localhost:33656/connect/token", $"http://localhost:4000/secrets/{TestSecretId}")] // Dev environment w/ trailing slash
    [InlineData("https://identity.bitwarden.com", "https://api.bitwarden.com", "https://identity.bitwarden.com/connect/token", $"https://api.bitwarden.com/secrets/{TestSecretId}")] // Cloud environment
    [InlineData("https://identity.bitwarden.com/", "https://api.bitwarden.com/", "https://identity.bitwarden.com/connect/token", $"https://api.bitwarden.com/secrets/{TestSecretId}")] // Cloud environment w/ trailing slash
    [InlineData("https://example.com/identity/", "https://example.com/api/", "https://example.com/identity/connect/token", $"https://example.com/api/secrets/{TestSecretId}")] // Self-host environment requires trailing slash
    public async Task UrlsAreConcatenatedProperly(string baseIdentityUrl, string baseApiUrl, string expectedConnectTokenRequest, string expectedRequestUrl)
    {
        var identityCalled = false;
        var testIdentityHttpHandler  = new TestHttpMessageHandler((request) =>
        {
            Assert.Equal(expectedConnectTokenRequest, request.RequestUri!.ToString());
            identityCalled = true;
            return new HttpResponseMessage
            {
                Content = JsonContent.Create(new
                {
                    access_token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiYWRtaW4iOnRydWUsImlhdCI6MTUxNjIzOTAyMn0.KMUFsIDTnFmyG3nMiGM6H9FNFUROf3wh7SmqJp-QV30",
                    expires_in = 3600,
                    token_type = "Bearer",
                    encrypted_payload = TestEncryptedString,
                }),
            };
        });

        var apiCalled = false;
        var testSecretsHttpHandler = new TestHttpMessageHandler((request) =>
        {
            Assert.Equal(expectedRequestUrl, request.RequestUri!.ToString());
            apiCalled = true;
            return new HttpResponseMessage
            {
                Content = JsonContent.Create(new
                {
                    id = TestSecretId,
                    key = TestEncryptedString,
                    value = TestEncryptedString,
                    revisionDate = DateTime.UtcNow,
                }),
            };
        });

        var identityClient = BitwardenIdentityClient.Create(new Uri(baseIdentityUrl), testIdentityHttpHandler);

        var client = BitwardenSecretsClient.Create(
            new Uri(baseApiUrl),
            identityClient,
            AccessToken.Parse("0.4eaea7be-6a0b-4c0b-861e-b033001532a9.ydNqCpyZ8E7a171FjZn89WhKE1eEQF:2WQh70hSQQZFXm+QteNYsg=="),
            testSecretsHttpHandler
        );

        var secret = await client.GetSecretAsync(Guid.Parse(TestSecretId));
        Assert.True(identityCalled);
        Assert.True(apiCalled);
    }

    private class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = send(request);
            return Task.FromResult(response);
        }
    }
}
