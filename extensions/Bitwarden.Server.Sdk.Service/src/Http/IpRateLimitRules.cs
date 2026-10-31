namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Which of a set of rules count a request, in which order, and in which bucket.
/// </summary>
/// <remarks>
/// <para>
/// With <c>EnableEndpointRateLimiting</c> (the default), a rule applies when its endpoint pattern matches the
/// request's <c>verb:path</c> or <c>*:path</c>, and counts each method and path apart: <c>*</c> and <c>post:*</c> give every
/// endpoint its own allowance, the bucket carrying the method and path. Without it, only <c>*</c> rules apply,
/// counting all of an address's requests together. Of the rules that apply, the most restrictive for each period counts
/// (the lowest limit, then the first endpoint), in order of period, shortest first; counting stops at the first over its
/// limit, so a refused request doesn't spend the longer periods' allowances. A limit of zero refuses every request.
/// </para>
/// <para>
/// The path is lower case, without a trailing slash, and with each run of slashes made one (routing serves those
/// spellings as one endpoint). <see cref="IpRateLimitRule.Grants"/> takes a rule out
/// for a request naming another grant before the most restrictive is chosen, and keeps its own count, so a request
/// it leaves out never spends its allowance.
/// </para>
/// </remarks>
public sealed class IpRateLimitRules
{
    private readonly List<(IpRateLimitRule Rule, string Pattern, TimeSpan Period, string Bucket)> _rules;

    /// <summary>
    /// Reads the rules; a period not in <see cref="IpRateLimitRule.Period"/>'s form fails here.
    /// </summary>
    /// <param name="rules">The rules, in <see cref="IpRateLimitRule"/>'s shape.</param>
    /// <param name="enableEndpointRateLimiting">Whether each method and path counts apart (<see cref="IpRateLimitOptions.EnableEndpointRateLimiting"/>).</param>
    public IpRateLimitRules(IEnumerable<IpRateLimitRule> rules, bool enableEndpointRateLimiting)
    {
        EnableEndpointRateLimiting = enableEndpointRateLimiting;
        _rules = [.. rules
            .Select(rule => (rule, Pattern: rule.Endpoint.Trim().ToLowerInvariant()))
            // Without endpoint limiting, only the rules for every request are read.
            .Where(rule => enableEndpointRateLimiting || rule.Pattern == "*")
            .Select(rule =>
            {
                // A rule's counts are its own: two rules differing only in their limit, or their grants,
                // don't share a window.
                var bucket = string.Join('\n', rule.Pattern, rule.rule.Period.Trim().ToLowerInvariant(), rule.rule.Limit,
                    string.Join(',', rule.rule.Grants.Select(grant => grant.ToLowerInvariant()).Order(StringComparer.Ordinal)));

                return (rule.rule, rule.Pattern, IpRateLimiting.Period(rule.rule.Period), bucket);
            })];
    }

    /// <summary>
    /// Whether each method and path counts apart (<see cref="IpRateLimitOptions.EnableEndpointRateLimiting"/>).
    /// </summary>
    public bool EnableEndpointRateLimiting { get; }

    /// <summary>
    /// Whether there are rules to apply.
    /// </summary>
    public bool Any => _rules.Count > 0;

    /// <summary>
    /// Whether a rule the request matches names grants, so its grant must be read before <see cref="Counting"/>.
    /// </summary>
    /// <param name="method">The request's method.</param>
    /// <param name="path">The request's path, as sent.</param>
    public bool NamesGrants(string? method, string? path)
    {
        var (verb, normalized, sent) = Endpoint(method, path);

        return _rules.Any(rule => rule.Rule.Grants.Count > 0 && Applies(rule.Pattern, verb, normalized, sent));
    }

    /// <summary>
    /// The rules that count the request, in the order they count it.
    /// </summary>
    /// <param name="method">The request's method.</param>
    /// <param name="path">The request's path, as sent.</param>
    /// <param name="grant">
    /// The single <c>grant_type</c> the request names, or null when it names none or several: a rule naming grants leaves
    /// out only a request naming exactly one grant, not its own.
    /// </param>
    public IReadOnlyList<IpRateLimitCount> Counting(string? method, string? path, string? grant = null)
    {
        var (verb, normalized, sent) = Endpoint(method, path);
        var endpoint = EnableEndpointRateLimiting ? $"\n{verb}\n{normalized}" : string.Empty;

        return [.. _rules
            .Where(rule => Applies(rule.Pattern, verb, normalized, sent))
            .Where(rule => rule.Rule.Grants.Count == 0 || grant is null || rule.Rule.Grants.Contains(grant, StringComparer.OrdinalIgnoreCase))
            // For each period the lowest limit, then the first endpoint pattern; shortest period first.
            .GroupBy(rule => rule.Rule.Period.Trim().ToLowerInvariant())
            .Select(period => period.OrderBy(rule => rule.Rule.Limit).ThenBy(rule => rule.Rule.Endpoint, StringComparer.InvariantCulture).First())
            .OrderBy(rule => rule.Period)
            .Select(rule => new IpRateLimitCount(rule.Rule, rule.Period, rule.Bucket + endpoint))];
    }

    /// <summary>
    /// Counts the request for one client address: each chosen rule in turn, stopping at
    /// the first over its limit.
    /// </summary>
    /// <param name="counters">Where the counts are kept.</param>
    /// <param name="address">The client's address.</param>
    /// <param name="method">The request's method.</param>
    /// <param name="path">The request's path, as sent.</param>
    /// <param name="grant">The single <c>grant_type</c> the request names, as <see cref="Counting"/> reads it.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>The rule the request is over, or null when it may go on.</returns>
    public async ValueTask<IpRateLimitCount?> TryTakeAsync(
        IRateLimitCounters counters, string address, string? method, string? path, string? grant = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var count in Counting(method, path, grant))
        {
            // A rule with no allowance blocks every request.
            if (count.Rule.Limit <= 0
                || !await counters.TryTakeAsync($"{count.Bucket}\n{address}", count.Rule.Limit, count.Period, cancellationToken))
            {
                return count;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an endpoint pattern matches the request: its <c>verb:path</c> or <c>*:path</c>.
    /// </summary>
    internal static bool Applies(string pattern, string verb, string normalized, string sent) =>
        IpRateLimiting.Matches(pattern, $"*:{normalized}") || IpRateLimiting.Matches(pattern, $"{verb}:{normalized}")
        // As sent, lower case: a rule matching either spelling counts, so normalizing never takes a request out of a rule.
        || IpRateLimiting.Matches(pattern, $"*:{sent}") || IpRateLimiting.Matches(pattern, $"{verb}:{sent}");

    private static (string Verb, string Normalized, string Sent) Endpoint(string? method, string? path) =>
        ((method ?? string.Empty).ToLowerInvariant(), IpRateLimiting.Normalized(path), (path ?? "/").ToLowerInvariant());
}
