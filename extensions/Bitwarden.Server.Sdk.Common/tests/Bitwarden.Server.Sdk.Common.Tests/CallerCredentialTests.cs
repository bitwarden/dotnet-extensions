using System.Text.Json;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class CallerCredentialTests
{
    [Fact]
    public void Serialize_WithDefaultOptions_IsWrittenAllCaps()
    {
        var json = JsonSerializer.Serialize(new { Credential = (CallerCredential?)CallerCredential.Machine });

        Assert.Equal("""{"Credential":"MACHINE"}""", json);
    }

    [Fact]
    public void Deserialize_WithDefaultOptions_ReadsAllCaps()
    {
        Assert.Equal(CallerCredential.Organization, JsonSerializer.Deserialize<CallerCredential>("\"ORGANIZATION\""));
    }
}
