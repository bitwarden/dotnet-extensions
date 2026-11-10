namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Thrown when encrypted data cannot be parsed.
/// </summary>
public sealed class InvalidEncryptedDataException : Exception
{
    /// <summary>
    /// Creates the exception with the parser's message and its underlying cause.
    /// </summary>
    public InvalidEncryptedDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
