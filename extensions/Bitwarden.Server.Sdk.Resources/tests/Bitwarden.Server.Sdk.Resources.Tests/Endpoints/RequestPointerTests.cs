using Bitwarden.Server.Sdk.Resources.Endpoints;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

/// <summary>
/// A failure's key, as binding or validation names it, becomes a pointer into the wire document,
/// where a resource's members sit under <c>attributes</c>.
/// </summary>
public sealed class RequestPointerTests
{
    [Theory]
    [InlineData("", "/data")]
    [InlineData("$", "/data")]
    [InlineData("Data.Email", "/data/attributes/email")]
    [InlineData("$.data.email", "/data/attributes/email")]
    [InlineData("Data.Address.Zip", "/data/attributes/address/zip")]
    [InlineData("Data.Id", "/data/id")]
    [InlineData("$.data.type", "/data/type")]
    [InlineData("Data", "/data")]
    [InlineData("Data[1].Name", "/data/1/attributes/name")]
    [InlineData("$.data[1].lid", "/data/1/lid")]
    [InlineData("$.data[0].id", "/data/0/id")]
    [InlineData("Data[2]", "/data/2")]
    [InlineData("Items[Home].Number", "/items/Home/number")]
    [InlineData("Items[a/b~c]", "/items/a~1b~0c")]
    [InlineData("Items[open", "/items/open")]
    [InlineData("Name", "/name")]
    public void For_Key_IntoTheWireDocument(string key, string expected)
    {
        Assert.Equal(expected, RequestPointer.For(key));
    }
}
