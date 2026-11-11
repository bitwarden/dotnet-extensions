using System.Text;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// The operations a contract marks as accepting idempotency keys, and the requests that go to them.
/// </summary>
public sealed class KeyedOperationsTests
{
    private const string Contract = """
        {
          "paths": {
            "/api/v1/widgets": {
              "post": { "operationId": "CreateWidget", "x-bitwarden-idempotency-key": true },
              "get": { "operationId": "ListWidgets" }
            },
            "/api/v1/widgets/{id}/actions/charge": {
              "post": { "operationId": "ChargeWidget", "x-bitwarden-idempotency-key": true }
            },
            "/api/v1/widgets/{id}": {
              "put": { "operationId": "ReplaceWidget", "x-bitwarden-idempotency-key": false },
              "delete": { "operationId": "DeleteWidget" }
            }
          }
        }
        """;

    private static KeyedOperations Keyed() => KeyedOperations.FromContract(new MemoryStream(Encoding.UTF8.GetBytes(Contract)));

    [Fact]
    public void FromContract_TakesOnlyTheOperationsMarkedTrue() => Assert.Equal(2, Keyed().Count);

    [Theory]
    [InlineData("POST", "http://widget.invalid/api/v1/widgets", true)]
    [InlineData("post", "http://widget.invalid/api/v1/widgets?validateOnly=false", true)]
    [InlineData("POST", "http://widget.invalid/API/V1/Widgets", true)]
    [InlineData("POST", "http://widget.invalid/api/v1/widgets/5/actions/charge", true)]
    // A callee reached under a path prefix its contract doesn't name.
    [InlineData("POST", "http://gateway.invalid/widget-service/api/v1/widgets", true)]
    [InlineData("PUT", "http://widget.invalid/api/v1/widgets", false)]
    [InlineData("PUT", "http://widget.invalid/api/v1/widgets/5", false)]
    [InlineData("DELETE", "http://widget.invalid/api/v1/widgets/5", false)]
    [InlineData("POST", "http://widget.invalid/api/v1/widgets/5/actions/refund", false)]
    [InlineData("POST", "http://widget.invalid/api/v1/gadgets", false)]
    public void Accepts_TheMethodAndPathOfAMarkedOperation(string method, string address, bool expected) =>
        Assert.Equal(expected, Keyed().Accepts(new HttpMethod(method), new Uri(address)));

    [Fact]
    public void Accepts_ARelativeAddress() =>
        Assert.True(Keyed().Accepts(HttpMethod.Post, new Uri("api/v1/widgets?x=1", UriKind.Relative)));

    [Fact]
    public void Accepts_NoAddress_Never() => Assert.False(Keyed().Accepts(HttpMethod.Post, null));

    [Fact]
    public void FromAssembly_WithoutAContract_IsNone()
    {
        var none = KeyedOperations.FromAssembly(typeof(KeyedOperations).Assembly);

        Assert.Same(KeyedOperations.None, none);
        Assert.False(none.Accepts(HttpMethod.Post, new Uri("http://widget.invalid/api/v1/widgets")));
    }

    [Fact]
    public void FromAssembly_ReadsTheEmbeddedContract() =>
        Assert.True(KeyedOperations.FromAssembly(typeof(KeyedOperationsTests).Assembly).Accepts(HttpMethod.Post, new Uri("http://callee.invalid/things")));

    [Fact]
    public void FromContract_WithoutPaths_IsEmpty() =>
        Assert.Equal(0, KeyedOperations.FromContract(new MemoryStream("{}"u8.ToArray())).Count);
}
