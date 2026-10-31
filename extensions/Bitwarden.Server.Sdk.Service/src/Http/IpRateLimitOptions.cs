using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Per-address request limits, read from the <c>IpRateLimitOptions</c> section.
/// </summary>
/// <remarks>
/// <para>
/// Off when self-hosted (<c>globalSettings:selfHosted</c>): limits apply only in Bitwarden's cloud.
/// Off when <see cref="Enabled"/> is false, which a development environment may set so its test
/// suites aren't throttled. No rules, no limiting.
/// </para>
/// <para>
/// The address is the connection's, which the platform's <c>ForwardedClientAddress</c> has already
/// replaced with what a trusted proxy saw, so nothing a client writes chooses its bucket. Counts are
/// fixed windows per rule, address and, with <see cref="EnableEndpointRateLimiting"/>, method and path
/// (<see cref="IpRateLimitRules"/>), kept where <see cref="IRateLimitCounters"/> keeps them: in this
/// process, or, with <c>globalSettings:distributedIpRateLimiting:redisConnectionString</c>, in a Redis
/// every instance shares (<see cref="DistributedIpRateLimitingOptions"/>).
/// </para>
/// </remarks>
public sealed class IpRateLimitOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "IpRateLimitOptions";

    /// <summary>
    /// Whether limits apply at all. Self-hosting turns them off whatever this says.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Addresses and networks (CIDR) never limited: a deployment's own service network, whose
    /// services call each other and fetch their own tokens.
    /// </summary>
    public List<string> IpWhitelist { get; set; } = [];

    /// <summary>
    /// Endpoints never limited: <c>verb:path</c> patterns in <see cref="IpRateLimitRule.Endpoint"/>'s
    /// form. A request matching one counts against no rule: for routes a service counts some other
    /// way, such as registrations counted once by the service that routes them.
    /// </summary>
    public List<string> EndpointWhitelist { get; set; } = [];

    /// <summary>
    /// Whether a rule counts each endpoint (method and path) a client calls apart, so <c>post:*</c> gives
    /// every <c>POST</c> route its own allowance. True by default. False, only rules whose
    /// <see cref="IpRateLimitRule.Endpoint"/> is <c>*</c> apply, and count all of an address's requests
    /// together (<see cref="IpRateLimitRules"/>).
    /// </summary>
    public bool EnableEndpointRateLimiting { get; set; } = true;

    /// <summary>
    /// The rules: for each period, the most restrictive a request matches counts it (<see cref="IpRateLimitRules"/>).
    /// </summary>
    public List<IpRateLimitRule> GeneralRules { get; set; } = [];

    /// <summary>
    /// The status a refusal answers with: <c>429</c> by default.
    /// </summary>
    public int HttpStatusCode { get; set; } = StatusCodes.Status429TooManyRequests;
}
