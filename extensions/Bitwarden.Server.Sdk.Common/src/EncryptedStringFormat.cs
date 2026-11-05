using System.Buffers.Text;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Whether a string is shaped like an encrypted string, as the Bitwarden server checks it.
/// </summary>
/// <remarks>
/// <c>{type}.{piece}|{piece}…</c>, with the pieces the type calls for: an AES IV decodes to 16 bytes
/// and an HMAC-SHA256 MAC to 32. Only the shape is checked. Nothing is decoded beyond base64, and
/// nothing is decrypted.
/// </remarks>
public static class EncryptedStringFormat
{
    private const int IvBytes = 16;
    private const int MacBytes = 32;
    private const int AnyLength = -1;

    private const string Base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>
    /// The encryption types an encrypted string may name, by name and number.
    /// </summary>
    private enum EncryptionType : byte
    {
        AesCbc256_B64 = 0,
        AesCbc256_HmacSha256_B64 = 2,
        CoseEncrypt0B64 = 7,
        Rsa2048_OaepSha256_B64 = 3,
        Rsa2048_OaepSha1_B64 = 4,
        Rsa2048_OaepSha256_HmacSha256_B64 = 5,
        Rsa2048_OaepSha1_HmacSha256_B64 = 6,
    }

    /// <summary>
    /// Each type's pieces and the decoded length of each.
    /// </summary>
    private static readonly Dictionary<EncryptionType, int[]> Pieces = new()
    {
        [EncryptionType.AesCbc256_B64] = [IvBytes, AnyLength], // iv|ct
        [EncryptionType.AesCbc256_HmacSha256_B64] = [IvBytes, AnyLength, MacBytes], // iv|ct|mac
        [EncryptionType.CoseEncrypt0B64] = [AnyLength], // COSE bytes
        [EncryptionType.Rsa2048_OaepSha256_B64] = [AnyLength], // rsaCt
        [EncryptionType.Rsa2048_OaepSha1_B64] = [AnyLength], // rsaCt
        [EncryptionType.Rsa2048_OaepSha256_HmacSha256_B64] = [AnyLength, AnyLength], // rsaCt|mac
        [EncryptionType.Rsa2048_OaepSha1_HmacSha256_B64] = [AnyLength, AnyLength], // rsaCt|mac
    };

    /// <summary>
    /// Whether <paramref name="value"/> is shaped like an encrypted string.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>Whether it is; false for anything that is not, never an exception.</returns>
    public static bool IsWellFormed(string value) => IsWellFormedCore(value);

    private static bool IsWellFormedCore(ReadOnlySpan<char> value)
    {
        if (!TrySplitBy(value, '.', out var header, out var rest))
        {
            // No header: the server's legacy forms, told apart by how many pieces there are.
            return ValidatePieces(rest, rest.Count('|') == 2
                ? Pieces[EncryptionType.AesCbc256_HmacSha256_B64]
                : Pieces[EncryptionType.AesCbc256_B64]);
        }

        if (!byte.TryParse(header, out var number))
        {
            return Enum.TryParse<EncryptionType>(header, out var named)
                && Pieces.TryGetValue(named, out var namedPieces)
                && ValidatePieces(rest, namedPieces);
        }

        return Pieces.TryGetValue((EncryptionType)number, out var expected) && ValidatePieces(rest, expected);
    }

    private static bool ValidatePieces(ReadOnlySpan<char> encrypted, ReadOnlySpan<int> lengths)
    {
        var rest = encrypted;

        for (var i = 0; i < lengths.Length; i++)
        {
            if (i == lengths.Length - 1)
            {
                return IsValidPiece(rest, lengths[i]) && rest.IndexOf('|') == -1;
            }

            if (!TrySplitBy(rest, '|', out var piece, out rest) || !IsValidPiece(piece, lengths[i]))
            {
                return false;
            }
        }

        return rest.IndexOf('|') == -1;
    }

    private static bool IsValidPiece(ReadOnlySpan<char> piece, int length) =>
        !piece.IsEmpty && IsValidBase64Permissive(piece) && (length == AnyLength || DecodedLength(piece) == length);

    /// <summary>
    /// How many bytes a valid base64 piece decodes to.
    /// </summary>
    private static int DecodedLength(ReadOnlySpan<char> piece)
    {
        var padding = piece[^1] == '=' ? (piece[^2] == '=' ? 2 : 1) : 0;
        return piece.Length / 4 * 3 - padding;
    }

    /// <summary>
    /// Whether a piece is padded base64, accepting padding bits that aren't zero as the server does.
    /// </summary>
    private static bool IsValidBase64Permissive(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length % 4 != 0)
        {
            return false;
        }

        var padding = value[^1] == '=' ? (value[^2] == '=' ? 2 : 1) : 0;
        if (padding == 0)
        {
            return Base64.IsValid(value);
        }

        var last = value[value.Length - padding - 1];
        var bits = Base64Chars.IndexOf(last);
        if (bits < 0)
        {
            return false;
        }

        var meaningful = bits & (padding == 2 ? 0b110000 : 0b111100);
        if (meaningful == bits)
        {
            return Base64.IsValid(value);
        }

        if (value.Length > 4 && !Base64.IsValid(value[..^4]))
        {
            return false;
        }

        Span<char> canonical = stackalloc char[4];
        value[^4..].CopyTo(canonical);
        canonical[4 - padding - 1] = Base64Chars[meaningful];
        return Base64.IsValid(canonical);
    }

    private static bool TrySplitBy(ReadOnlySpan<char> input, char separator, out ReadOnlySpan<char> chunk, out ReadOnlySpan<char> rest)
    {
        var index = input.IndexOf(separator);
        if (index == -1)
        {
            chunk = default;
            rest = input;
            return false;
        }

        chunk = input[..index];
        rest = input[(index + 1)..];
        return true;
    }
}
