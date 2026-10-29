namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A service this one depends on could not be reached, did not answer in time, or said it was
/// unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Answered with <c>503</c>, as the standards require when a dependency is down, unreachable or
/// times out — and never with the <c>500</c> an unrecognised exception gets, which says this
/// service has a bug. A dependency's outage reported as a bug in every service that calls it sends
/// whoever is paged looking for the wrong thing.
/// </para>
/// <para>
/// The caller is told which service, because that is what they need to act on, and nothing of how
/// it is reached: no address, no host name, no exception text. <see cref="Detail"/> is written
/// here, from the service's name and what happened, and never from anything the failure carried.
/// </para>
/// <para>
/// Raised by the SDK's clients. A dependency that answered <c>500</c> is not unavailable — it is up,
/// and has a bug of its own — so it does not become one of these.
/// </para>
/// </remarks>
public sealed class ServiceUnavailableException : Exception
{
    /// <param name="service">The service depended on, as it is named in configuration.</param>
    /// <param name="what">What happened, as a phrase completing "The {service} service …".</param>
    /// <param name="innerException">The failure, for the log.</param>
    public ServiceUnavailableException(string service, string what, Exception? innerException = null)
        : base($"The {service} service {what}.", innerException)
    {
        Service = service;
        Detail = $"The {service} service {what}.";
    }

    /// <summary>The service depended on.</summary>
    public string Service { get; }

    /// <summary>What the caller is told: which service, and what happened.</summary>
    public string Detail { get; }

    /// <summary>It could not be reached at all.</summary>
    public static ServiceUnavailableException Unreachable(string service, Exception innerException) =>
        new(service, "could not be reached", innerException);

    /// <summary>It did not answer within the time allowed.</summary>
    public static ServiceUnavailableException TimedOut(string service, TimeSpan timeout) =>
        new(service, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"did not answer within {timeout.TotalSeconds:0.###} seconds"));

    /// <summary>It answered that it, or something behind it, was unavailable.</summary>
    public static ServiceUnavailableException Answered(string service, int status) =>
        new(service, $"answered {status}");
}
