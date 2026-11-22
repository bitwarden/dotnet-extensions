using System.Text;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// The Bearer scheme accepts only tokens addressed to this service by its own name, beside any audience a service has
/// added on purpose, and validates the audience whatever it was set to before.
/// </summary>
public sealed class ServiceAudienceTests
{
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-signing-key-that-signs-nothing-real-anywhere"));

    private static JwtBearerOptions PostConfigured(TokenValidationParameters parameters, string? name = JwtBearerDefaults.AuthenticationScheme)
    {
        var options = new JwtBearerOptions { TokenValidationParameters = parameters };
        new ServiceAudience(new Identity("widget-service")).PostConfigure(name, options);
        return options;
    }

    [Fact]
    public void PostConfigure_TheBearerScheme_ValidatesThisServicesAudience()
    {
        var parameters = PostConfigured(new TokenValidationParameters { ValidateAudience = false }).TokenValidationParameters;

        Assert.True(parameters.ValidateAudience);
        Assert.Null(parameters.ValidAudience);
        Assert.Equal(["widget-service"], parameters.ValidAudiences);
    }

    // What a service added on purpose is kept, its own name joins it once, and blanks are dropped.
    [Fact]
    public void PostConfigure_KeepsTheAudiencesAlreadyNamed()
    {
        var parameters = PostConfigured(new TokenValidationParameters
        {
            ValidAudience = "gadget-service",
            ValidAudiences = ["web", "", "widget-service"],
        }).TokenValidationParameters;

        Assert.Equal(["web", "widget-service", "gadget-service"], parameters.ValidAudiences);
        Assert.Null(parameters.ValidAudience);
    }

    // Another scheme is left as it was configured.
    [Theory]
    [InlineData("Other")]
    [InlineData(null)]
    public void PostConfigure_AnotherScheme_Untouched(string? name)
    {
        var parameters = PostConfigured(new TokenValidationParameters { ValidateAudience = false }, name).TokenValidationParameters;

        Assert.False(parameters.ValidateAudience);
        Assert.Null(parameters.ValidAudiences);
    }

    // A token naming several audiences is accepted when one is this service's, as RFC 7519 reads aud.
    [Theory]
    [InlineData(true, "widget-service")]
    [InlineData(true, "gadget-service", "widget-service")]
    [InlineData(false, "gadget-service")]
    [InlineData(false, "Widget-service")]
    public async Task PostConfigure_ATokensAudience_ValidatedAgainstThisService(bool valid, params string[] audiences)
    {
        var parameters = PostConfigured(new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidIssuer = "https://identity.example.test",
            IssuerSigningKey = Key,
        }).TokenValidationParameters;

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://identity.example.test",
            Claims = new Dictionary<string, object> { ["aud"] = audiences },
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
        });

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.Equal(valid, result.IsValid);
    }

    private sealed class Identity(string name) : IServiceIdentity
    {
        public string Name => name;

        public string Title => name;
    }
}
