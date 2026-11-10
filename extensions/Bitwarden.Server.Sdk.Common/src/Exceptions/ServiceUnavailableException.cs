namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A service this one depends on could not be reached, did not answer in time, or said it was
/// unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Answered with <c>503</c> rather than <c>500</c>, so a dependency's outage is not reported as a
/// bug in every service that calls it.
/// </para>
/// <para>
/// <see cref="Detail"/> names the service and what happened, and nothing of how it is reached: no
/// address, host name or exception text.
/// </para>
/// <para>
/// A dependency that answered <c>500</c> is up and has a bug of its own, so it does not become one
/// of these.
/// </para>
/// </remarks>
public sealed class ServiceUnavailableException : Exception
{
    /// <summary>
    /// The service depended on is unavailable.
    /// </summary>
    /// <param name="service">The service depended on, as it is named in configuration.</param>
    /// <param name="what">What happened, as a phrase completing "The {service} service …".</param>
    /// <param name="innerException">The failure, for the log.</param>
    public ServiceUnavailableException(string service, string what, Exception? innerException = null)
        : base($"The {service} service {what}.", innerException)
    {
        Service = service;
        Detail = $"The {service} service {what}.";
    }

    /// <summary>
    /// The service depended on.
    /// </summary>
    public string Service { get; }

    /// <summary>
    /// What the caller is told: which service, and what happened.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// It could not be reached at all.
    /// </summary>
    /// <param name="service">The service depended on.</param>
    /// <param name="innerException">The failure, for the log.</param>
    public static ServiceUnavailableException Unreachable(string service, Exception innerException) =>
        new(service, "could not be reached", innerException);

    /// <summary>
    /// It did not answer within the time allowed.
    /// </summary>
    /// <param name="service">The service depended on.</param>
    /// <param name="timeout">The time allowed.</param>
    public static ServiceUnavailableException TimedOut(string service, TimeSpan timeout) =>
        new(service, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"did not answer within {timeout.TotalSeconds:0.###} seconds"));

    /// <summary>
    /// It answered that it, or something behind it, was unavailable.
    /// </summary>
    /// <param name="service">The service depended on.</param>
    /// <param name="status">The status it answered with.</param>
    public static ServiceUnavailableException Answered(string service, int status) =>
        new(service, $"answered {status}");
}
