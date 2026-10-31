using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The browser origins a service accepts cross-origin requests from, and the CORS policy that
/// admits them.
/// </summary>
/// <remarks>
/// <para>
/// CORS is an access-control boundary: it decides which web pages a browser lets read a
/// service's answers. So it is off unless a deployment names the origins, it never admits any
/// origin, and it never admits credentials: a session travels in <c>Authorization</c>, which a
/// page has to attach on purpose, and never in a cookie a browser would attach on its own.
/// </para>
/// <para>
/// Configured by origin rather than tied to <see cref="ProductServiceAttribute"/>, because a service
/// below the product layer can have browser-facing protocol endpoints too, such as Identity's prelogin,
/// token and discovery endpoints.
/// </para>
/// </remarks>
internal static class BrowserOrigins
{
    /// <summary>
    /// Where the origins are configured: an array of <c>scheme://host[:port]</c>.
    /// </summary>
    internal const string ConfigurationKey = "Service:BrowserOrigins";

    /// <summary>
    /// The policy's name, so it never displaces a default policy a service declares.
    /// </summary>
    internal const string PolicyName = "Bitwarden.BrowserOrigins";

    /// <summary>
    /// What a wildcard entry starts its host with, as in <c>http://*.localhost:8888</c>.
    /// </summary>
    private const string WildcardLabel = "*.";

    /// <summary>
    /// The methods the platform's standard operations use, and nothing else.
    /// </summary>
    internal static readonly string[] Methods = ["GET", "POST", "PUT", "DELETE"];

    /// <summary>
    /// The request headers a page may send: those Bitwarden's web client, its SDK and the
    /// documentation UI attach.
    /// </summary>
    /// <remarks>
    /// The caller-context headers (<c>Bitwarden-Organization</c>, <c>Bitwarden-User</c>,
    /// <c>Bitwarden-Provider</c>, <c>Bitwarden-Context</c>) are deliberately absent, everywhere. A
    /// browser sends its token and nothing else about whose request it is: a
    /// product service takes that from the session and refuses the headers <c>400</c>, and a
    /// domain service believes them only from another service. Leaving them out means a page's
    /// preflight fails before such a request is ever sent.
    /// </remarks>
    internal static readonly string[] Headers =
    [
        "Accept",
        "Authorization",
        "Bitwarden-Client-Name",
        "Bitwarden-Client-Version",
        "Bitwarden-Package-Type",
        "Cache-Control",
        "Content-Type",
        "Device-Identifier",
        "Device-Type",
        "Is-Prerelease",
        "Pragma",
        "User-Agent",
        "X-Device-Identifier",
        "X-Requested-With",
    ];

    /// <summary>
    /// Reads and checks the configured origins.
    /// </summary>
    /// <returns>The origins, empty when none are configured.</returns>
    /// <exception cref="InvalidOperationException">An entry is not an origin a browser sends.</exception>
    internal static IReadOnlyList<string> Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationKey);

        // A single value is one origin, so an environment variable can name one without indexing.
        IReadOnlyList<string> entries = section.Value is { } single
            ? [single]
            : [.. section.GetChildren().Select(child => child.Value ?? string.Empty)];

        foreach (var entry in entries)
        {
            Check(entry);
        }

        return entries;
    }

    /// <summary>
    /// Registers the policy admitting <paramref name="origins"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="origins">The configured origins.</param>
    internal static void AddPolicy(IServiceCollection services, IReadOnlyList<string> origins)
    {
        services.AddCors(options => options.AddPolicy(PolicyName, policy => Build(policy, origins)));
    }

    private static void Build(CorsPolicyBuilder policy, IReadOnlyList<string> origins)
    {
        policy
            .WithOrigins([.. origins])
            .WithMethods(Methods)
            .WithHeaders(Headers);

        // Only when asked for. Matching subdomains widens who is admitted, so an exact list stays
        // exact.
        if (origins.Any(IsWildcard))
        {
            policy.SetIsOriginAllowedToAllowWildcardSubdomains();
        }

        // No AllowCredentials, ever: see the remarks on this type.
    }

    private static bool IsWildcard(string origin) =>
        origin.Contains("://" + WildcardLabel, StringComparison.Ordinal);

    private static void Check(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || entry.Trim() == "*")
        {
            throw Invalid(entry, "Name each origin; admitting any origin is not supported.");
        }

        // The wildcard is allowed in one place only, as the host's first label.
        var schemeEnd = entry.IndexOf("://", StringComparison.Ordinal);
        var star = entry.IndexOf('*', StringComparison.Ordinal);
        if (star >= 0
            && (schemeEnd < 0
                || star != schemeEnd + 3
                || !entry.AsSpan(star).StartsWith(WildcardLabel, StringComparison.Ordinal)
                || entry.IndexOf('*', star + 1) >= 0))
        {
            throw Invalid(entry, $"A wildcard may only lead the host, as in 'http://{WildcardLabel}example.com'.");
        }

        // Parsed with the wildcard swapped for a label, so the rest is checked like any origin.
        var parseable = star >= 0 ? entry.Remove(star, 1).Insert(star, "wildcard") : entry;

        if (!Uri.TryCreate(parseable, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.GetLeftPart(UriPartial.Authority), parseable, StringComparison.OrdinalIgnoreCase))
        {
            // The browser's Origin header is exactly scheme, host and any non-default port. An
            // entry with a path, a trailing slash or a default port would never match it, and
            // would fail silently rather than here.
            throw Invalid(entry, "Write it as a browser sends it: scheme, host and any non-default port, with no path or trailing slash.");
        }
    }

    private static InvalidOperationException Invalid(string entry, string reason) =>
        new($"'{entry}' in {ConfigurationKey} is not a browser origin. {reason}");
}
