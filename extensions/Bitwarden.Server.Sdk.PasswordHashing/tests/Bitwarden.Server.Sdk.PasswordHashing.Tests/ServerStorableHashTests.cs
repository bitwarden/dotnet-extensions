namespace Bitwarden.Server.Sdk.PasswordHashing.Tests;

public sealed class ServerStorableHashTests
{
    private const string Secret = "r5CFRR+n9NQI8a525FY+0BPR0HGOjVJX0cR1KEMnIOo=";

    [Fact]
    public void ToString_HidesValue()
    {
        Assert.DoesNotContain(Secret, new ServerStorableHash(Secret).ToString());
    }

    [Fact]
    public void SameValue_IsEqual()
    {
        Assert.Equal(new ServerStorableHash(Secret), new ServerStorableHash(Secret));
    }
}
