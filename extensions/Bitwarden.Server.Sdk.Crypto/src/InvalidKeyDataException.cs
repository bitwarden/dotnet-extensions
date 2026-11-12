namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Thrown when key service <c>data</c> is refused.
/// </summary>
public sealed class InvalidKeyDataException : Exception
{
    /// <summary>
    /// Creates the exception with why the data was refused and the parser's underlying cause.
    /// </summary>
    public InvalidKeyDataException(KeyDataError error, Exception innerException)
        : base($"Key data refused: {error}.", innerException)
    {
        Error = error;
    }

    /// <summary>Why the data was refused.</summary>
    public KeyDataError Error { get; }
}
