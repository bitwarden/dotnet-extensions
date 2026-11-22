using Bitwarden.Server.Sdk.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Refuses a plain-HTTP authority for the bearer handler, which is where Identity's keys come from.
/// </summary>
internal sealed class HttpsAuthority : IValidateOptions<JwtBearerOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtBearerOptions options) =>
        name != JwtBearerDefaults.AuthenticationScheme
        || options.Authority is null
        || options.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{InternalTls.Section}:RequireHttps is set, and the bearer handler's authority, '{options.Authority}', is not https.");
}
