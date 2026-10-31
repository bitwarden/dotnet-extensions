namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// One limit: at most <see cref="Limit"/> requests matching <see cref="Endpoint"/> in any <see cref="Period"/>.
/// </summary>
public sealed class IpRateLimitRule
{
    /// <summary>
    /// The endpoint pattern: <c>*</c> for every request, or <c>verb:path</c> with <c>*</c> for any
    /// run of characters and <c>?</c> for any one (<c>post:*</c>, <c>post:/connect/token</c>, <c>*:/accounts/*</c>,
    /// <c>post:/api/v1/two-factor-providers/*/actions/verify-user</c>). Matched whole against the request's
    /// <c>verb:path</c> and <c>*:path</c>, without case.
    /// </summary>
    public string Endpoint { get; set; } = "*";

    /// <summary>
    /// The period: a number and <c>s</c>, <c>m</c>, <c>h</c> or <c>d</c> (<c>1s</c>, <c>60m</c>, <c>1d</c>).
    /// </summary>
    public string Period { get; set; } = "1m";

    /// <summary>
    /// How many requests the period allows.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// For a token endpoint: only requests whose form names one of these <c>grant_type</c>s count.
    /// Empty, and every matching request counts. Lets a rule count sign-ins without counting a
    /// token endpoint's other grants, such as a session exchanged for a product's token.
    /// </summary>
    public List<string> Grants { get; set; } = [];
}
