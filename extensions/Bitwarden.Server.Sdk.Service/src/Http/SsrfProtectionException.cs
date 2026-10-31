namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Exception thrown when an SSRF protection check fails.
/// </summary>
public class SsrfProtectionException : Exception
{
    /// <summary>
    /// Refuses a request, saying why.
    /// </summary>
    /// <param name="message">Why.</param>
    public SsrfProtectionException(string message) : base(message) { }
    /// <summary>
    /// Refuses a request, saying why and what failed.
    /// </summary>
    /// <param name="message">Why.</param>
    /// <param name="innerException">What failed.</param>
    public SsrfProtectionException(string message, Exception innerException) : base(message, innerException) { }
}
