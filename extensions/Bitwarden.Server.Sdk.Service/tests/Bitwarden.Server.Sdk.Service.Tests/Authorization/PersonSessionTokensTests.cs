using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// A product service accepts only a token naming a person, failing any other at authentication, except a credential
/// an operation declares it accepts; and keeps whatever validation was configured before it.
/// </summary>
public sealed class PersonSessionTokensTests
{
    private static readonly AuthenticationScheme Scheme =
        new(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

    private static JwtBearerOptions Configured(JwtBearerEvents? events = null)
    {
        var options = new JwtBearerOptions { Events = events! };
        new PersonSessionTokens().PostConfigure(JwtBearerDefaults.AuthenticationScheme, options);
        return options;
    }

    private static async Task<TokenValidatedContext> Validate(
        JwtBearerOptions options, IEnumerable<Claim> claims, params object[] metadata)
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "operation"));
        var validated = new TokenValidatedContext(http, Scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };

        await options.Events.OnTokenValidated(validated);
        return validated;
    }

    private static Claim Subject(string value) => new("sub", value);

    [Fact]
    public async Task ATokenNamingAPerson_Accepted()
    {
        var validated = await Validate(Configured(), [Subject(Guid.NewGuid().ToString())]);

        Assert.Null(validated.Result);
    }

    // A subject that is not an identifier names nobody the platform knows; no subject names nobody at all.
    [Theory]
    [InlineData("internal.some-service")]
    [InlineData("send:5e4d0000-0000-4000-8000-000000000001")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ATokenNamingNoPerson_Failed(string? subject)
    {
        var validated = await Validate(Configured(), subject is null ? [] : [Subject(subject)]);

        Assert.False(validated.Result!.Succeeded);
        Assert.Equal(PersonSessionTokens.Failure, validated.Result.Failure!.Message);
    }

    // --- The credentials an operation may declare -----------------------------------------------------------------

    [Fact]
    public async Task AnOrganizationCredential_WhereDeclared_Accepted()
    {
        var validated = await Validate(
            Configured(),
            [new Claim("organization", Guid.NewGuid().ToString())],
            new AcceptsOrganizationCredentialAttribute("api.organization"));

        Assert.Null(validated.Result);
    }

    [Fact]
    public async Task AnOrganizationCredential_WhereNotDeclared_Failed()
    {
        var validated = await Validate(Configured(), [new Claim("organization", Guid.NewGuid().ToString())]);

        Assert.False(validated.Result!.Succeeded);
    }

    // A Send's credential declared elsewhere is no organization credential, nor the other way round.
    [Fact]
    public async Task AnOrganizationCredential_WhereASendCredentialIsDeclared_Failed()
    {
        var validated = await Validate(
            Configured(), [new Claim("organization", Guid.NewGuid().ToString())], new AcceptsSendCredentialAttribute());

        Assert.False(validated.Result!.Succeeded);
    }

    [Fact]
    public async Task ASendCredential_WhereDeclared_Accepted()
    {
        var validated = await Validate(
            Configured(),
            [Subject("send:5e4d0000-0000-4000-8000-000000000001"), new Claim(SendCredential.Claim, "5e4d0000-0000-4000-8000-000000000001")],
            new AcceptsSendCredentialAttribute());

        Assert.Null(validated.Result);
    }

    [Fact]
    public async Task ASendCredential_WhereNotDeclared_Failed()
    {
        var validated = await Validate(
            Configured(),
            [new Claim(SendCredential.Claim, "5e4d0000-0000-4000-8000-000000000001")],
            new AcceptsOrganizationCredentialAttribute("api.organization"));

        Assert.False(validated.Result!.Succeeded);
    }

    // --- Wrapping what was there ----------------------------------------------------------------------------------

    // The validation configured before still runs, first.
    [Fact]
    public async Task ThePreviousHandler_RunsFirst()
    {
        var ran = false;
        var options = Configured(new JwtBearerEvents { OnTokenValidated = _ => { ran = true; return Task.CompletedTask; } });

        await Validate(options, [Subject(Guid.NewGuid().ToString())]);

        Assert.True(ran);
    }

    // Something earlier already decided, and its answer stands, a token naming nobody included.
    [Fact]
    public async Task AnEarlierAnswer_Stands()
    {
        var options = Configured(new JwtBearerEvents
        {
            OnTokenValidated = validated =>
            {
                validated.Success();
                return Task.CompletedTask;
            },
        });

        var validated = await Validate(options, []);

        Assert.True(validated.Result!.Succeeded);
    }

    [Fact]
    public async Task NoEventsConfigured_AddsItsOwn()
    {
        var validated = await Validate(Configured(events: null), []);

        Assert.False(validated.Result!.Succeeded);
    }

    // Only the bearer scheme a service authenticates people with: another scheme's options are left as they were.
    [Fact]
    public void AnotherScheme_LeftAlone()
    {
        var events = new JwtBearerEvents();
        var before = events.OnTokenValidated;
        var options = new JwtBearerOptions { Events = events };

        new PersonSessionTokens().PostConfigure("another-scheme", options);

        Assert.Same(before, options.Events.OnTokenValidated);
    }
}
