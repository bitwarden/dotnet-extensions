using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Refuses to start a service whose client is not configured.
/// </summary>
/// <param name="serviceName">The service being called, for the message.</param>
/// <remarks>
/// Names the configuration keys that would have supplied what is missing. A client fails at
/// startup, where the person who can fix it is looking, rather than on the first request that
/// happened to need it.
/// </remarks>
internal sealed class BitwardenClientOptionsValidator(string serviceName)
    : IValidateOptions<BitwardenClientOptions>
{
    public ValidateOptionsResult Validate(string? name, BitwardenClientOptions options)
    {
        if (name != serviceName)
        {
            return ValidateOptionsResult.Skip;
        }

        var missing = new List<string>();

        // The keys Conventions reads, in its order, so the message names every one that would have supplied the value.
        var key = serviceName.ToLowerInvariant();
        Require(options.BaseAddress, nameof(options.BaseAddress),
            $"services:{key}:https:0", $"services:{key}:http:0", $"{key}:baseUrl");
        Require(options.CallerName, nameof(options.CallerName), "Bitwarden:ServiceName");
        Require(options.IdentityUri, nameof(options.IdentityUri),
            "globalSettings:baseServiceUri:internalIdentity", "globalSettings:baseServiceUri:identity",
            "Authentication:Schemes:Bearer:Authority");
        // A key, or else a secret: the key is sent in the secret's place.
        if (string.IsNullOrWhiteSpace(options.CallerKey))
        {
            Require(options.CallerSecret, nameof(options.CallerSecret), "globalSettings:internalIdentityKey");
            if (string.IsNullOrWhiteSpace(options.CallerSecret))
            {
                missing.Add($"Or give it a private key instead: Services:{serviceName}:CallerKey or Bitwarden:ClientKey.");
            }
        }
        else if (!ClientAssertion.IsReadable(options.CallerKey))
        {
            return ValidateOptionsResult.Fail(
                $"The {serviceName} client's CallerKey is not a PKCS#8 P-256 or RSA (2048 bits or more) private key, base64.");
        }

        if (missing.Count == 0 && !Uri.IsWellFormedUriString(options.BaseAddress, UriKind.Absolute))
        {
            return ValidateOptionsResult.Fail(
                $"The {serviceName} client's BaseAddress is not an absolute URI: '{options.BaseAddress}'.");
        }

        if (missing.Count == 0 && options.RequireHttps
            && (!IsHttps(options.BaseAddress) || !IsHttps(options.IdentityUri)))
        {
            return ValidateOptionsResult.Fail(
                $"Tls:RequireHttps is set, and the {serviceName} client's BaseAddress or IdentityUri is not https.");
        }

        if (options.Retries is < 0 or > 2 || options.RetryDelay < TimeSpan.Zero
            || options.BreakAfter < 0 || options.BreakFor < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"The {serviceName} client's Retries must be 0 to 2, BreakAfter 0 or more, and RetryDelay and BreakFor "
                + "not negative.");
        }

        // A zero deadline cancels every call at once, and a token used past its life, or renewed every second, is
        // never what was meant.
        if ((options.Timeout <= TimeSpan.Zero && options.Timeout != Timeout.InfiniteTimeSpan)
            || options.RefreshAt is not (> 0 and < 1))
        {
            return ValidateOptionsResult.Fail(
                $"The {serviceName} client's Timeout must be positive (or infinite), and RefreshAt more than 0 and less "
                + "than 1.");
        }

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"The {serviceName} client is not configured. " + string.Join(" ", missing));

        static bool IsHttps(string address) =>
            Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;

        void Require(string value, string member, params string[] conventionalKeys)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(
                    $"{member} is missing; set Services:{serviceName}:{member}, or {string.Join(", ", conventionalKeys)}.");
            }
        }
    }
}
