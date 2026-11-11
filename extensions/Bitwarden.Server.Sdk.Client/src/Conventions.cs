using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Where a client's settings come from when nobody says.
/// </summary>
/// <remarks>
/// A service deployed the ordinary way should need no client configuration at all. Each key below
/// is tried in order, from the most specific to the most conventional, and the first that answers
/// wins.
/// </remarks>
internal static class Conventions
{
    public static void Apply(BitwardenClientOptions options, IConfiguration configuration, string serviceName)
    {
        var section = $"{BitwardenClientOptions.Section}:{serviceName}";
        var key = serviceName.ToLowerInvariant();

        options.BaseAddress = First(configuration,
            $"{section}:BaseAddress",
            // What .NET Aspire publishes for a referenced service.
            $"services:{key}:https:0",
            $"services:{key}:http:0",
            $"{key}:baseUrl") ?? string.Empty;

        options.CallerName = First(configuration,
            $"{section}:CallerName",
            "Bitwarden:ServiceName")
            ?? EntryAssemblyName();

        // The callee's own name, which its client package registers it under.
        options.Audience = First(configuration, $"{section}:Audience") ?? serviceName;

        options.IdentityUri = First(configuration,
            $"{section}:IdentityUri",
            "globalSettings:baseServiceUri:internalIdentity",
            "globalSettings:baseServiceUri:identity",
            // The same key the serving side reads, so one setting configures both directions.
            "Authentication:Schemes:Bearer:Authority") ?? string.Empty;

        // The deployment's own rule, for every client alike (Bitwarden.Server.Sdk.Http.InternalTls).
        options.RequireHttps = bool.TryParse(
            configuration[$"{Bitwarden.Server.Sdk.Http.InternalTls.Section}:{nameof(Bitwarden.Server.Sdk.Http.InternalTls.RequireHttps)}"],
            out var required) && required;

        options.CallerSecret = First(configuration,
            $"{section}:CallerSecret",
            "globalSettings:internalIdentityKey") ?? string.Empty;

        // A service's own key, for every client it calls with (Bitwarden.Server.Sdk.Client.ClientAssertion).
        options.CallerKey = First(configuration,
            $"{section}:CallerKey",
            "Bitwarden:ClientKey") ?? string.Empty;
    }

    private static string? First(IConfiguration configuration, params string[] keys) =>
        keys.Select(key => configuration[key])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <remarks>
    /// The last resort for the caller's own name. Trimmed of the organisation prefix so an
    /// assembly called <c>Bitwarden.Reference.Service</c> authenticates as <c>reference</c>.
    /// </remarks>
    private static string EntryAssemblyName()
    {
        var name = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        var parts = name.Split('.');

        return parts.Length > 1 ? parts[1].ToLowerInvariant() : name.ToLowerInvariant();
    }
}
