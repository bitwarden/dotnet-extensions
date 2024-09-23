namespace Bitwarden.Extensions.Hosting.Exceptions;

/// <summary>
/// Exception for when a request is invalid.
/// </summary>
public class BadRequestException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadRequestException"/> class.
    /// </summary>
    /// <param name="message">Error message.</param>
    public BadRequestException(string message)
        : base(message)
    { }
}
