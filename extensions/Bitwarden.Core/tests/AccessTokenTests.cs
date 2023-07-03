namespace Bitwarden.Core.Tests;

public class AccessTokenTests
{
    [Fact]
    public void Test()
    {
        var at = AccessToken.Parse("0.4eaea7be-6a0b-4c0b-861e-b033001532a9.ydNqCpyZ8E7a171FjZn89WhKE1eEQF:2WQh70hSQQZFXm+QteNYsg==");
        Assert.Equal(0, at.Version);
        Assert.Equal(Guid.Parse("4eaea7be-6a0b-4c0b-861e-b033001532a9"), at.ClientId);
        Assert.NotNull(at.ClientSecret);
        Assert.NotNull(at.EncryptionKey);
    }
}
