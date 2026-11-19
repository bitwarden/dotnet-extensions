using System.Runtime.CompilerServices;

namespace Bitwarden.Server.Sdk.CryptoPrimitives;

// 32 bytes held inline, so CacheSecret and CacheKey are values with no heap allocation and default is all zeros.
[InlineArray(Length)]
internal struct Bytes32
{
    public const int Length = 32;

    private byte _element0;

    public static Bytes32 From(ReadOnlySpan<byte> bytes, string name, string paramName)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"A {name} is {Length} bytes, not {bytes.Length}.", paramName);
        }

        var result = new Bytes32();
        bytes.CopyTo(result);

        return result;
    }

    public static Bytes32 FromBase64(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Decode into one spare byte, so a value longer than 32 bytes fails the length check.
        Span<byte> bytes = stackalloc byte[Length + 1];
        if (!Convert.TryFromBase64String(value, bytes, out var written) || written != Length)
        {
            throw new FormatException($"A {name} is base64 of {Length} bytes.");
        }

        return From(bytes[..Length], name, nameof(value));
    }
}
