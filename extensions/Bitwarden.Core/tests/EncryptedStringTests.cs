using Bitwarden.Core;

namespace Bitwarden.Extensions.Configuration.Tests;

public class EncryptedStringTests
{
    [Fact]
    public void Decrypt_Works()
    {
        var key = SymmetricCryptoKey.Parse("UY4B5N4DA4UisCNClgZtRr6VLy9ZF5BXXC7cDZRqourKi4ghEMgISbCsubvgCkHf5DZctQjVot11/vVvN9NNHQ==");

        var encryptedString = EncryptedString.Parse("2.pDHeLbEbD3jWDmnFqYwI7g==|FAN55mPW4MZL+P9c4VkqIRDoAXdcHqHv4KpO50bwcvY=|yFJuxPEJD3oOqZ0v8U+WSKIX5Kr+/d0sCPi8Jwxb+ek=");
        var decryptedText = encryptedString.DecryptToString(key);
        Assert.Equal("encrypted_test_string", decryptedText);
    }
}
