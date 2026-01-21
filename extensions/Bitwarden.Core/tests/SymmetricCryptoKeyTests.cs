using Bitwarden.Core;

namespace Bitwarden.Extensions.Configuration.Tests;

public class AccessTokenTests
{
    // [Fact]
    // public void Parse_Works()
    // {
    //     var accessToken = AccessToken.Parse("0.ec2c1d46-6a4b-4751-a310-af9601317f2d.C2IgxjjLF7qSshsbwe8JGcbM075YXw:X8vbvA0bduihIDe/qrzIQQ==", null);
    // }

    [Fact]
    public void StretchKey_Works()
    {
        var key = SymmetricCryptoKey.Create("&/$%F1a895g67HlX"u8, "test_key"u8, default);
        Assert.Equal("4PV6+PcmF2w7YHRatvyMcVQtI7zvCyssv/wFWmzjiH6Iv9altjmDkuBD1aagLVaLezbthbSe+ktR+U6qswxNnQ==",
            key.ToString());
    }
}
