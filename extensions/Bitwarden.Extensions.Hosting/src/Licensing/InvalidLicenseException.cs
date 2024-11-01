namespace Bitwarden.Extensions.Hosting.Licensing;

/// <summary>
/// A set of reasons explaining why a license was invalid.
/// </summary>
public enum InvalidLicenseReason
{
    /// <summary>
    /// The given license was in an invalid format and could not be read further.
    /// </summary>
    InvalidFormat,

    /// <summary>
    /// The given license may have been valid previously but has expired and should no longer be used.
    /// </summary>
    Expired,

    /// <summary>
    /// The license was signed with a different key than the one that was used to verify it.
    /// </summary>
    WrongKey,

    /// <summary>
    /// The license is invalid for an unknown reason, checks logs for additional details.
    /// </summary>
    Unknown,
}

/// <summary>
/// The exception that is thrown when a license is invalid and cannot be verified.
/// </summary>
public class InvalidLicenseException : Exception
{
    private const string DefaultMessage = "The license is invalid and cannot be trusted.";

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidLicenseException"/>.
    /// </summary>
    /// <param name="reason"></param>
    public InvalidLicenseException(InvalidLicenseReason reason)
        : base(DefaultMessage)
    {
        Reason = reason;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidLicenseException"/>.
    /// </summary>
    /// <param name="reason"></param>
    /// <param name="message"></param>
    public InvalidLicenseException(InvalidLicenseReason reason, string? message)
        : this(reason, message, null)
    { }

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidLicenseException"/>.
    /// </summary>
    /// <param name="reason"></param>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public InvalidLicenseException(InvalidLicenseReason reason, string? message, Exception? innerException)
        : base(message ?? DefaultMessage, innerException)
    {
        Reason = reason;
    }

    /// <summary>
    /// The reason the license was found to be invalid.
    /// </summary>
    public InvalidLicenseReason Reason { get; }
}
