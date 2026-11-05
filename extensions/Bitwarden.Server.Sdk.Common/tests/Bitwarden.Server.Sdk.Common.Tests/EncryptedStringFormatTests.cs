namespace Bitwarden.Server.Sdk.Common.Tests;

/// <summary>
/// <see cref="EncryptedStringFormat.IsWellFormed"/> gives the Bitwarden server's verdict on every string below:
/// the server's own test cases and further strings run through the server's check. The server accepts the
/// legacy headerless forms, a type by number or by name, an AES IV of 16 bytes and a MAC of 32, and padding bits it
/// doesn't check; it refuses plain text, a removed or unknown type, the wrong number of pieces and pieces of the
/// wrong length. The strings are made-up placeholders that protect nothing: only the shape of Protected Data is
/// judged.
/// </summary>
public sealed class EncryptedStringFormatTests
{
    /// <summary>
    /// The server's own test cases, with its verdicts.
    /// </summary>
    [Theory]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==|Y3Q=", true)]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==|Y3Q=|AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", true)]
    [InlineData("0.AAECAwQFBgcICQoLDA0ODw==|QmFzZTY0UGFydA==", true)]
    [InlineData("2.AAECAwQFBgcICQoLDA0ODw==|QmFzZTY0UGFydA==|AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", true)]
    [InlineData("3.QmFzZTY0UGFydA==", true)]
    [InlineData("4.QmFzZTY0UGFydA==", true)]
    [InlineData("5.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", true)]
    [InlineData("6.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", true)]
    [InlineData("7.QmFzZTY0UGFydA==", true)]
    [InlineData("0.AAECAwQFBgcICQoLDA0ODw==|AAAA", true)]
    [InlineData("AAECAwQFBgcICQoLDA0OD/==|lGD=", true)]
    [InlineData("2.AAECAwQFBgcICQoLDA0OD/==|lGD=|AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh/=", true)]
    [InlineData("0.AAECAwQFBgcICQoLDA0ODw==|QmFzZTY0UGFydB==", true)]
    [InlineData("Test", false)]
    [InlineData("Hello World", false)]
    [InlineData("SecretPassword123", false)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("|", false)]
    [InlineData("||", false)]
    [InlineData("!|!", false)]
    [InlineData("254.QmFzZTY0UGFydA==", false)]
    [InlineData("0.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", false)]
    [InlineData("1.AAECAwQFBgcICQoLDA0ODw==|QmFzZTY0UGFydA==|AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", false)]
    [InlineData("2.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==|QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", false)]
    [InlineData("3.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", false)]
    [InlineData("4.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", false)]
    [InlineData("5.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", false)]
    [InlineData("6.QmFzZTY0UGFydA==|QmFzZTY0UGFydA==|QmFzZTY0UGFydA==", false)]
    [InlineData("0.AA!!AB==|Y3Q=", false)]
    [InlineData("0.AAAAB==|Y3Q=", false)]
    [InlineData("0.====|Y3Q=", false)]
    [InlineData("2.AAECAw==|Y3Q=|AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", false)]
    [InlineData("0.AAECAw==|Y3Q=", false)]
    [InlineData("AAECAw==|Y3Q=", false)]
    [InlineData("0.AAAA|Y3Q=", false)]
    [InlineData("0.AAECAwQFBgcICQoLDA0ODxA=|Y3Q=", false)]
    [InlineData("2.AAECAwQFBgcICQoLDA0ODw==|Y3Q=|AAECAw==", false)]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==|Y3Q=|AAECAw==", false)]
    [InlineData("2.lGD=|lGD=|lGD=", false)]
    [InlineData("P|CfDJ8AAAAAAAAAAAAAAAAAAAAAAAA", false)]
    // Named types: type 1 is gone and type 7's old name isn't the server's; and pieces too short for an IV or a MAC.
    [InlineData("AesCbc128_HmacSha256_B64.AAECAwQFBgcICQoLDA0ODw==|Y3Q=|AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", false)]
    [InlineData("XChaCha20Poly1305_B64.QmFzZTY0UGFydA==", false)]
    [InlineData("CoseEncrypt0B64.QmFzZTY0UGFydA==", true)]
    [InlineData("2.AAAA|AAAA|AAAA", false)]
    [InlineData("AAAA|AAAA", false)]
    public void IsWellFormed_TheServersOwnTestCases_IsTheServersVerdict(string value, bool server)
    {
        Assert.Equal(server, EncryptedStringFormat.IsWellFormed(value));
    }

    /// <summary>
    /// The server's verdicts on further strings, found by running them through the server's check: whitespace
    /// around the type, named types in any case, extra and missing pieces, and padding.
    /// </summary>
    public static TheoryData<string, bool> ServerVerdicts => new()
    {
        { "", false },
        { "\u0009+1 .AAAA|AAAA|AAAA", false },
        { "\u00095.+oADWC2mEEQGFH8IsGsy7w==|7A==", true },
        { "\u00096.lf8i+AU4RMHDutkyqcLjmg==|ARaPN2c=", true },
        { "\u00097.wb4=", true },
        { "\u00097.zEc=", true },
        { "\u0009aescbc256_b64\u000a\u200b.AAAA|AAAA", false },
        { "\u000a\u000bAesCbc128_HmacSha256_B64 .AAAA|AAAA|AAAA", false },
        { "\u000a-1\u2028.AAAA", false },
        { "\u000a1.zwI=|nwjm|wAOxAb5Urb/sM00FP8slEQ==", false },
        { "\u000a2.Sg==|nQ==|mv5tSbs+4iENTOZXEBwlPQ==", false },
        { "\u000a4.3A==", true },
        { "\u000a4.O2u7IFo1RsYXiUI68qJN28SmX86ajFDuC8IJuchrIKQ=", true },
        { "\u000a4.gHQs", true },
        { "\u000aBogus\ufeff.AAAA", false },
        { "\u000aRsa2048_OaepSha1_B64\u000b\u200b.AAAA|AAAA|AAAA", false },
        { "\u000a\u0662\u3000.AAAA|AAAA", false },
        { "\u000b\u0009Bogus\u0085\u0009.AAAA|AAAA", false },
        { "\u000b\u000bRsa2048_OaepSha1_B64\u0085\u0085.AAAA|AAAA", false },
        { "\u000b1 2\u000b.AAAA|AAAA", false },
        { "\u000b1 2 .AAAA|AAAA|AAAA", false },
        { "\u000b255\u200b.AAAA|AAAA", false },
        { "\u000b255\u2028.AAAA", false },
        { "\u000b4\u000a.AAAA|AAAA|AAAA", false },
        { "\u000b5\ufeff.AAAA|AAAA|AAAA", false },
        { "\u000b8\u200b.AAAA|AAAA", false },
        { "\u000b\u200bRsa2048_OaepSha256_HmacSha256_B64\u000a\u0085.AAAA|AAAA", false },
        { "\u000b\u200baescbc256_b64\u0009\ufeff.AAAA|AAAA", false },
        { "\u000d0.jsw=|WPA=", false },
        { "\u000d4.UGRuRAA=", true },
        { " 5.+oADWC2mEEQGFH8IsGsy7w==|7A==", true },
        { " 6.T9M=|DkqzVP80czRxy3LKQxxA5w==", true },
        { " \u0085AesCbc256_HmacSha256_B64\u0009.AAAA|AAAA", false },
        { " \u3000AesCbc128_HmacSha256_B64 \u200b.AAAA", false },
        { " \u3000Rsa2048_OaepSha1_B64\u000a\u2028.AAAA|AAAA", false },
        { "+1\u3000.AAAA|AAAA|AAAA", false },
        { ".", false },
        { "0.H2UeavgFUGtnLZEZcerq2g==|3xwX", true },
        { "0.jsw=|WPA=|KWqu", false },
        { "0.lpKITw==|3Q\u000a==", false },
        { "007\u0009.AAAA", true },
        { "04.H2UeavgFUGtnLZEZcerq2g==|3xwX", false },
        { "2 .6w==|tVI=", false },
        { "2.", false },
        { "2. AAAA|AAAA|AAAA", false },
        { "2.A===|AAAA|AAAA", false },
        { "2.AA AA|AAAA|AAAA", false },
        { "2.AA==|AA==|AA==", false },
        { "2.AAA=|AAA=|AAA=", false },
        { "2.AAAA |AAAA|AAAA", false },
        { "2.AAAA=|AAAA|AAAA", false },
        { "2.AAAA|AAAA|A A=", false },
        { "2.AAAA|AAAA|AA =", false },
        { "2.AAAA|AAAA|AA= =", false },
        { "2.AAAA|AAAA|AA==\u000a", false },
        { "2.AAAA|AAAA|AA=A", false },
        { "2.AAAA|AAAA|AAAA", false },
        { "2.AAAA|AAAA|AAAA ", false },
        { "2.AAAA|AAAA|AB==", false },
        { "2.AAAA|AAAA|ABC=", false },
        { "2.AB==|AAAA|AAAA", false },
        { "2.Sg==|nQ==|mv5tSb+4iENTOZXEBwlPQ==", false },
        { "2.spBwbA\u0009==|4Q==|SQ==", false },
        { "2.|", false },
        { "3.2Q\u0009==", false },
        { "3.5gw=", true },
        { "3.AAAA", true },
        { "3.X5VbUP5zutjpFHW4NtAf+bXbOeRWP2WigA++sO+e67E=\u00a0", false },
        { "3.dV7O8QlW\u000aJbZNMEHW5W/R6w==", false },
        { "4\u0009.XY0=", true },
        { "4.7Z6GuQc =", false },
        { "4.AB==", true },
        { "4.O2u7IFo1RsYXiUI68qJN28SmX86ajFDuC8IJuchrIKQ=", true },
        { "4.wYQwXRX=", true },
        { "5.+oADWC2mEEQGFH8IsGsy7w==|7A==", true },
        { "5.9FDM |tg==", false },
        { "5.LyZ9dA==|HrtvpzWETSWmhqGhNlHAnA==", true },
        { "5.YTkSdIZA94uq8eUcgJW+51g==|Ef8=", false },
        { "6.mHdDbNo=|\u000dF9dI", false },
        { "7.9A==\u0009", false },
        { "7.AAAA", true },
        { "7.AAAA|AAAA", false },
        { "7.JEY=", true },
        { "7.ayqwohqmQoDrS4KLfdUSaQ\u2028==", false },
        { "7.zEc=", true },
        { "AAAA", false },
        { "AAAA|AAAA", false },
        { "AAAA|AAAA|AAAA", false },
        { "AAAA|AAAA|AAAA|AAAA", false },
        { "Sg==|nQ==|mv5tSbs+4iENTOZXEBwlPQ==", false },
        { "sA==|XWTG4TY=", false },
        { "|", false },
        { "||", false },
        { "\u0085\u000bAesCbc256_B64\u2028\u0085.AAAA", false },
        { "\u0085 Rsa2048_OaepSha256_B64, Bogus\u200b.AAAA|AAAA", false },
        { "\u0085-0\u0009.AAAA|AAAA|AAAA", false },
        { "\u00851 2\u000a.AAAA|AAAA", false },
        { "\u00851\u00a0.AAAA", false },
        { "\u00a0\u000bAesCbc128_HmacSha256_B64\u0009\u3000.AAAA|AAAA|AAAA", false },
        { "\u00a0 Bogus,\u0009Bogus\u2028,\ufeffRsa2048_OaepSha1_B64\ufeff.AAAA|AAAA", false },
        { "\u00a0-0\u000b.AAAA|AAAA", false },
        { "\u00a0-0\u0085.AAAA|AAAA|AAAA", false },
        { "\u00a02.spBwbA==|4Q==|SQ==", false },
        { "\u00a0255\u3000.AAAA|AAAA|AAAA", false },
        { "\u00a04 .AAAA", true },
        { "\u00a07.WQ==", true },
        { "\u00a07.sg==", true },
        { "\u00a07.zQ==", true },
        { "\u200b\u000bAesCbc256_HmacSha256_B64\u000b\u0085.AAAA", false },
        { "\u2028\u000bRsa2048_OaepSha256_B64\u00a0.AAAA", true },
        { "\u20280.cg==|QmU=", false },
        { "\u20280x2\u000a.AAAA|AAAA|AAAA", false },
        { "\u2028255\u000b.AAAA|AAAA|AAAA", false },
        { "\u20283.ei4SGVQ=", true },
        { "\u20287 .AAAA|AAAA", false },
        { "\u2028\u0662\u200b.AAAA", false },
        { "\u2028\ufeffAesCbc128_HmacSha256_B64 \u00a0.AAAA", false },
        { "\u2028\ufeffAesCbc256_HmacSha256_B64,\u000bAesCbc256_B64\u000a\u0085.AAAA", false },
        { "\u3000\u0009aescbc256_b64\u3000\u00a0.AAAA|AAAA|AAAA", false },
        { "\u3000+1\u00a0.AAAA|AAAA|AAAA", false },
        { "\u3000+1\u3000.AAAA|AAAA", false },
        { "\u3000-0\u000b.AAAA|AAAA", false },
        { "\u3000256\u0009.AAAA|AAAA", false },
        { "\u3000Rsa2048_OaepSha256_B64\u000b\u00a0.AAAA|AAAA|AAAA", false },
        { "\u3000\u2028Rsa2048_OaepSha1_B64\u2028\u000b.AAAA|AAAA|AAAA", false },
        { "\u3000\ufeffBogus\u0009,\u200bAesCbc256_B64\u2028,\u000aBogus\u200b\u3000.AAAA|AAAA|AAAA", false },
        { "\ufeff255\u0009.AAAA|AAAA|AAAA", false },
        { "\ufeff\ufeffAesCbc256_HmacSha256_B64\u2028 .AAAA", false },
    };

    [Theory]
    [MemberData(nameof(ServerVerdicts))]
    public void IsWellFormed_FurtherStrings_IsTheServersVerdict(string value, bool server)
    {
        Assert.Equal(server, EncryptedStringFormat.IsWellFormed(value));
    }

    [Theory]
    [InlineData("2.AAAAAAAAAAAAAAAAAAAAAA==|AAAA|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", true)]
    [InlineData("2.AAAAAAAAAAAAAAAAAAAAAA==|AAAA|AAAA", false)]
    [InlineData("4.AAAA", true)]
    [InlineData("7.AAAA", true)]
    [InlineData("9.AAAA", false)]
    [InlineData("2.AAAAAAAAAAAAAAAAAAAAAA==|AAA|AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("", false)]
    // A type's name is matched case-sensitively, and names joined by a comma combine their numbers (3 | 4 is 7),
    // as the server parses them.
    [InlineData("aescbc256_b64.AAECAwQFBgcICQoLDA0ODw==|Y3Q=", false)]
    [InlineData("AesCbc256_B64.AAECAwQFBgcICQoLDA0ODw==|Y3Q=", true)]
    [InlineData("Rsa2048_OaepSha256_B64,Rsa2048_OaepSha1_B64.AAAA", true)]
    public void IsWellFormed_AsTheServersAttribute(string value, bool wellFormed)
    {
        Assert.Equal(wellFormed, EncryptedStringFormat.IsWellFormed(value));
    }

    [Fact]
    public void IsWellFormed_ArbitraryInput_NeverThrows()
    {
        // A fixed seed keeps the run repeatable. Each string starts with a header of the kinds the server's
        // verdicts above exercise, then random pieces of base64 characters, padding, separators and whitespace.
        string[] headers = ["", "0.", "2.", "7.", "255.", " 4 .", "-1.", "AesCbc256_B64.", "Rsa2048_OaepSha1_B64, Bogus.", "\u00a0\u0662."];
        const string alphabet = "AQgw09+/=|. \t\u00a0";
        var random = new Random(20261105);
        var buffer = new char[60];

        for (var i = 0; i < 20_000; i++)
        {
            var length = random.Next(buffer.Length + 1);
            for (var j = 0; j < length; j++)
            {
                buffer[j] = alphabet[random.Next(alphabet.Length)];
            }

            var value = headers[random.Next(headers.Length)] + new string(buffer, 0, length);
            var exception = Record.Exception(() => EncryptedStringFormat.IsWellFormed(value));
            Assert.True(exception is null, $"\"{value}\" threw {exception}");
        }
    }
}
