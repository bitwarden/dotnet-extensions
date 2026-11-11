using System.Security.Claims;
using System.Text;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// Context tokens signed as Identity would sign them, and a request from another service presenting one, for the
/// tests of what verifies them.
/// </summary>
internal static class ContextTokens
{
    public const string Issuer = "https://identity.example.test";

    public const string Service = "widget-service";

    public const string Caller = "internal.gadget-service";

    public static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-signing-key-that-signs-nothing-real-anywhere"))
    {
        KeyId = "current",
    };

    /// <summary>
    /// A context token, by default delegated, addressed to <see cref="Service"/>, issued to <see cref="Caller"/>, valid for
    /// five minutes, with an empty context object.
    /// </summary>
    /// <param name="claims">Payload claims to set, or with a null value to remove.</param>
    public static string Sign(
        Guid? organization = null,
        Guid? user = null,
        Guid? provider = null,
        string mode = ContextToken.Delegated,
        int hops = 1,
        string audience = Service,
        string issuer = Issuer,
        string type = ContextToken.Type,
        SecurityKey? key = null,
        DateTime? expires = null,
        bool signed = true,
        IReadOnlyDictionary<string, object?>? claims = null)
    {
        object actor = new Dictionary<string, object> { ["sub"] = "internal.first-service" };
        for (var hop = 1; hop < hops; hop++)
        {
            actor = new Dictionary<string, object> { ["sub"] = $"internal.service-{hop}", [ContextToken.ActorClaim] = actor };
        }

        var payload = new Dictionary<string, object>
        {
            [ContextToken.ClientClaim] = Caller,
            [ContextToken.ModeClaim] = mode,
            [ContextToken.ActorClaim] = actor,
            [ContextToken.ContextClaim] = """{"v":1}""",
        };

        if (organization is not null)
        {
            payload[ContextToken.OrganizationClaim] = organization.Value.ToString();
        }

        if (user is not null)
        {
            payload[ContextToken.UserClaim] = user.Value.ToString();
        }

        if (provider is not null)
        {
            payload[ContextToken.ProviderClaim] = provider.Value.ToString();
        }

        foreach (var (name, value) in claims ?? new Dictionary<string, object?>())
        {
            if (value is null)
            {
                payload.Remove(name);
            }
            else
            {
                payload[name] = value;
            }
        }

        var expiry = expires ?? DateTime.UtcNow.AddMinutes(5);

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            TokenType = type,
            IssuedAt = expiry.AddMinutes(-10),
            NotBefore = expiry.AddMinutes(-10),
            Expires = expiry,
            Claims = payload,
            SigningCredentials = signed ? new SigningCredentials(key ?? Key, SecurityAlgorithms.HmacSha256) : null,
        });
    }

    /// <summary>
    /// A call from <paramref name="client"/>, authenticated by its service token, to a service trusting
    /// <see cref="Key"/> and <see cref="Issuer"/>.
    /// </summary>
    /// <param name="configure">Changes to the Bearer scheme's options, after the defaults.</param>
    /// <param name="services">Further services.</param>
    public static DefaultHttpContext Request(
        string client = Caller,
        Action<JwtBearerOptions>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IServiceIdentity>(new Identity(Service));

        collection.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = Key,
                ValidIssuer = Issuer,
            };
            configure?.Invoke(options);
        });
        services?.Invoke(collection);

        return new DefaultHttpContext
        {
            RequestServices = collection.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(InternalCaller.ClientClaim, client)], "Bearer")),
            Response = { Body = new MemoryStream() },
        };
    }

    private sealed class Identity(string name) : IServiceIdentity
    {
        public string Name => name;

        public string Title => name;
    }
}
