using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Counts requests against <see cref="IpRateLimitOptions"/> and refuses those over a limit.
/// </summary>
internal sealed class IpRateLimiting
{
    private readonly IpRateLimitOptions _options;
    private readonly bool _enabled;
    private readonly IpRateLimitRules _rules;
    private readonly List<IPNetwork> _whitelist;
    private readonly List<string> _endpointWhitelist;
    private readonly ILogger<IpRateLimiting> _logger;
    private readonly IIpRateLimitRefusal _refusal;
    private readonly IRateLimitCounters _counters;

    public IpRateLimiting(
        IOptions<IpRateLimitOptions> options,
        IConfiguration configuration,
        IIpRateLimitRefusal refusal,
        IRateLimitCounters counters,
        ILogger<IpRateLimiting> logger)
    {
        _options = options.Value;
        _counters = counters;
        _refusal = refusal;
        _logger = logger;
        _rules = new IpRateLimitRules(_options.GeneralRules, _options.EnableEndpointRateLimiting);
        _enabled = _options.Enabled && !configuration.GetValue(SelfHosting.Key, false) && _rules.Any;
        _whitelist = [.. _options.IpWhitelist.Select(Network)];
        _endpointWhitelist = [.. _options.EndpointWhitelist.Select(endpoint => endpoint.Trim().ToLowerInvariant())];
    }

    public bool Enabled => _enabled;

    /// <summary>
    /// Counts the request against the rules that apply to it; the rule it is over, or null.
    /// </summary>
    public async Task<(IpRateLimitRule Rule, TimeSpan Period)?> TryTakeAsync(HttpContext http)
    {
        if (!_enabled || http.Connection.RemoteIpAddress is not { } address)
        {
            return null;
        }

        address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (_whitelist.Any(network => network.Contains(address)))
        {
            return null;
        }

        var verb = http.Request.Method.ToLowerInvariant();
        var path = Normalized(http.Request.Path.Value);

        // A whitelisted endpoint must match the normalized path, as verb:path or *:path.
        if (_endpointWhitelist.Any(endpoint => Matches(endpoint, $"{verb}:{path}") || Matches(endpoint, $"*:{path}")))
        {
            return null;
        }

        // A request the limiter can't resolve to a single grant (none, or the key repeated, which the endpoint's binder
        // reads as its first) counts against a rule naming grants, as a rule without grants would count it.
        var grant = _rules.NamesGrants(http.Request.Method, http.Request.Path.Value) ? await GrantAsync(http) : null;

        return await _rules.TryTakeAsync(_counters, address.ToString(), http.Request.Method, http.Request.Path.Value, grant, http.RequestAborted)
            is { } exceeded ? (exceeded.Rule, exceeded.Period) : null;
    }

    /// <summary>
    /// Refuses the request: the configured status, <c>Retry-After</c>, and the sentence, in the body the service's
    /// <see cref="IIpRateLimitRefusal"/> writes (the platform's error document unless it registers another).
    /// </summary>
    public Task RefuseAsync(HttpContext http, IpRateLimitRule rule, TimeSpan period)
    {
        _logger.LogInformation("Refused a request to {Path}: over the limit of {Limit} per {Period}.", http.Request.Path, rule.Limit, rule.Period);

        http.Response.StatusCode = _options.HttpStatusCode;
        http.Response.Headers.RetryAfter = ((int)Math.Ceiling(period.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return _refusal.WriteAsync(http, $"Slow down! Too many requests. Try again in {rule.Period}.");
    }

    /// <summary>
    /// The error code of a refusal.
    /// </summary>
    public const string TooManyRequests = "too-many-requests";

    /// <summary>
    /// The single grant the request's form names, or null when it names none or more than one.
    /// </summary>
    private static async Task<string?> GrantAsync(HttpContext http)
    {
        if (!http.Request.HasFormContentType)
        {
            return null;
        }

        // Buffered, so the endpoint reads the same form after this.
        http.Request.EnableBuffering();
        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(http.RequestAborted);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // A form the server will not read (over its form limits, or cut short) names no grant, and counts; the
            // endpoint answers it. A refused body (BadHttpRequestException) is an IOException.
            return null;
        }
        finally
        {
            http.Request.Body.Position = 0;
        }

        // Exactly one value, or none the limiter can trust: StringValues.ToString() would join a
        // repeated key's values ("password,password"), which names no grant any rule lists.
        return form["grant_type"] is { Count: 1 } values ? values[0] : null;
    }

    /// <summary>
    /// The request's path as rules are matched against it: lower case, each run of slashes one slash, and no
    /// trailing slash. Endpoint routing serves <c>/connect/token/</c> as <c>/connect/token</c>, so the limiter
    /// counts every spelling routing treats as the same endpoint against the same rules; a spelling routing
    /// doesn't serve is answered 404 whether counted or not.
    /// </summary>
    internal static string Normalized(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var builder = new System.Text.StringBuilder(path.Length);

        foreach (var character in path)
        {
            if (character == '/' && builder.Length > 0 && builder[^1] == '/')
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        if (builder.Length > 1 && builder[^1] == '/')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>
    /// A pattern where <c>*</c> stands for any run of characters and <c>?</c> for any one, matched whole.
    /// </summary>
    internal static bool Matches(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, resume = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[v]))
            {
                p++;
                v++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = v;
            }
            else if (star >= 0)
            {
                // The last star takes one more character, and matching resumes after it.
                p = star + 1;
                v = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>
    /// A rule's period text: <c>1s</c>, <c>1m</c>, <c>60m</c>, <c>1h</c>, <c>1d</c>.
    /// </summary>
    internal static TimeSpan Period(string text)
    {
        var value = text.Trim().ToLowerInvariant();

        if (value.Length < 2 || !int.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
        {
            throw new InvalidOperationException($"'{text}' is not a rate limit period: a number and s, m, h or d.");
        }

        return value[^1] switch
        {
            's' => TimeSpan.FromSeconds(count),
            'm' => TimeSpan.FromMinutes(count),
            'h' => TimeSpan.FromHours(count),
            'd' => TimeSpan.FromDays(count),
            _ => throw new InvalidOperationException($"'{text}' is not a rate limit period: a number and s, m, h or d."),
        };
    }

    private static IPNetwork Network(string text)
    {
        if (IPNetwork.TryParse(text, out var network))
        {
            return network;
        }

        if (IPAddress.TryParse(text, out var address))
        {
            return new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
        }

        throw new InvalidOperationException($"'{text}' in IpRateLimitOptions:IpWhitelist is neither an address nor a network.");
    }
}
