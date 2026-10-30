using System.Text.Json;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class CallerCredentialJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new CallerCredentialJsonConverter() } };

    [Theory]
    [InlineData(CallerCredential.User, "\"USER\"")]
    [InlineData(CallerCredential.Organization, "\"ORGANIZATION\"")]
    [InlineData(CallerCredential.Machine, "\"MACHINE\"")]
    [InlineData(CallerCredential.System, "\"SYSTEM\"")]
    public void WriteThenRead_EveryCredential_RoundTripsAllCaps(CallerCredential credential, string json)
    {
        Assert.Equal(json, JsonSerializer.Serialize(credential, Options));
        Assert.Equal(credential, JsonSerializer.Deserialize<CallerCredential>(json, Options));
    }

    [Theory]
    [InlineData("\"machine\"")]
    [InlineData("\"ADMIN\"")]
    [InlineData("1")]
    public void Read_AnythingElse_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CallerCredential>(json, Options));
    }
}
