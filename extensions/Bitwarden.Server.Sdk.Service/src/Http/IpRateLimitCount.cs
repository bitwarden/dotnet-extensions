namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// A rule chosen to count a request: the rule, its period, and the bucket its count is kept in.
/// </summary>
/// <param name="Rule">The rule.</param>
/// <param name="Period">Its period.</param>
/// <param name="Bucket">
/// What it counts, without the client's address: the rule, and with <see cref="IpRateLimitOptions.EnableEndpointRateLimiting"/>
/// the request's method and path.
/// </param>
public readonly record struct IpRateLimitCount(IpRateLimitRule Rule, TimeSpan Period, string Bucket);
