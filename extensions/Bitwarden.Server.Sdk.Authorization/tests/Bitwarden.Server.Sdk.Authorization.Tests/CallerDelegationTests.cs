using System.Text;
using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// Verifies a call's context token: signed by the keys and issuer the Bearer scheme trusts, of the context token's
/// type, unexpired, addressed to this service and issued to the caller presenting it; the plain headers only ever a
/// copy of what it says.
/// </summary>
public sealed class CallerDelegationTests
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");
    private static readonly Guid User = new("a1000000-0000-4000-8000-00000000000a");
    private static readonly Guid Provider = new("b2000000-0000-4000-8000-00000000000b");

    private static async Task<(VerifiedContext? Context, CallerDelegation.Refused? Refusal)> Verify(
        string? token,
        HttpContext? http = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        http ??= ContextTokens.Request();

        if (token is not null)
        {
            http.Request.Headers[BitwardenHeaders.Delegation] = token;
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            http.Request.Headers[name] = value;
        }

        return await CallerDelegation.VerifyAsync(http);
    }

    private static void AssertUnsigned((VerifiedContext? Context, CallerDelegation.Refused? Refusal) verified)
    {
        Assert.Null(verified.Context);
        var refusal = Assert.IsType<CallerDelegation.Refused>(verified.Refusal);
        Assert.Equal(StatusCodes.Status401Unauthorized, refusal.Status);
        Assert.Equal(BitwardenHeaders.Delegation, refusal.Header);
    }

    private static void AssertMalformed((VerifiedContext? Context, CallerDelegation.Refused? Refusal) verified, string header)
    {
        Assert.Null(verified.Context);
        var refusal = Assert.IsType<CallerDelegation.Refused>(verified.Refusal);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.Status);
        Assert.Equal(header, refusal.Header);
    }

    [Fact]
    public async Task Verify_ADelegatedToken_IsWhatItSays()
    {
        var token = ContextTokens.Sign(Organization, User, Provider, claims: new Dictionary<string, object?>
        {
            [ContextToken.ContextClaim] = """{"v":1,"deviceType":"9"}""",
        });

        var verified = await Verify(token);

        Assert.Null(verified.Refusal);
        var context = Assert.IsType<VerifiedContext>(verified.Context);
        Assert.Equal(Organization, context.Organization);
        Assert.Equal(User, context.User);
        Assert.Equal(Provider, context.Provider);
        Assert.Equal("9", context.Context.DeviceType);
        Assert.Equal(token, context.Token);
        Assert.Equal(ContextToken.Delegated, context.Mode);
        Assert.Equal(1, context.Hops);
    }

    [Fact]
    public async Task Verify_AnAssertedToken_KeepsItsMode()
    {
        var verified = await Verify(ContextTokens.Sign(Organization, mode: ContextToken.Asserted));

        Assert.Equal(ContextToken.Asserted, Assert.IsType<VerifiedContext>(verified.Context).Mode);
    }

    // Owners a token doesn't name are absent, not empty identifiers.
    [Fact]
    public async Task Verify_ATokenNamingNoOwner_HasNone()
    {
        var context = Assert.IsType<VerifiedContext>((await Verify(ContextTokens.Sign())).Context);

        Assert.Null(context.Organization);
        Assert.Null(context.User);
        Assert.Null(context.Provider);
    }

    // One per service in the act chain, so a service can tell a first hop from a further one.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Verify_Hops_AreTheActChainsLength(int hops)
    {
        var context = Assert.IsType<VerifiedContext>((await Verify(ContextTokens.Sign(user: User, hops: hops))).Context);

        Assert.Equal(hops, context.Hops);
    }

    // No act chain, or one that isn't an object, still counts as the first hop.
    [Theory]
    [InlineData("none")]
    [InlineData("text")]
    public async Task Verify_NoActorChain_CountsAsOneHop(string kind)
    {
        object? actor = kind == "none" ? null : "not-an-object";
        var token = ContextTokens.Sign(
            user: User,
            claims: new Dictionary<string, object?> { [ContextToken.ActorClaim] = actor });

        Assert.Equal(1, Assert.IsType<VerifiedContext>((await Verify(token)).Context).Hops);
    }

    // A call asserting nothing needs nothing signed: its context is empty.
    [Fact]
    public async Task Verify_NoContextAtAll_NeitherContextNorRefusal()
    {
        var verified = await Verify(null);

        Assert.Null(verified.Context);
        Assert.Null(verified.Refusal);
    }

    [Theory]
    [InlineData(BitwardenHeaders.Organization)]
    [InlineData(BitwardenHeaders.User)]
    [InlineData(BitwardenHeaders.Provider)]
    [InlineData(BitwardenHeaders.Context)]
    public async Task Verify_APlainHeaderWithoutAToken_Unauthenticated(string header)
    {
        var value = header == BitwardenHeaders.Context ? """{"v":1}""" : User.ToString();

        AssertUnsigned(await Verify(null, headers: new Dictionary<string, string> { [header] = value }));
    }

    [Fact]
    public async Task Verify_AnEmptyContextToken_Unauthenticated()
    {
        AssertUnsigned(await Verify(string.Empty));
    }

    [Fact]
    public async Task Verify_NotAToken_Unauthenticated()
    {
        AssertUnsigned(await Verify("not-a-token"));
    }

    [Fact]
    public async Task Verify_SignedWithAnotherKey_Unauthenticated()
    {
        var forger = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-forgers-signing-key-that-nobody-here-trusts"))
        {
            KeyId = "current",
        };

        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, key: forger)));
    }

    [Fact]
    public async Task Verify_Unsigned_Unauthenticated()
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, signed: false)));
    }

    [Fact]
    public async Task Verify_Expired_Unauthenticated()
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, expires: DateTime.UtcNow.AddMinutes(-10))));
    }

    [Fact]
    public async Task Verify_NoExpiry_Unauthenticated()
    {
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ContextTokens.Issuer,
            Audience = ContextTokens.Service,
            TokenType = ContextToken.Type,
            Claims = new Dictionary<string, object>
            {
                [ContextToken.ClientClaim] = ContextTokens.Caller,
                [ContextToken.ModeClaim] = ContextToken.Delegated,
                [ContextToken.ContextClaim] = """{"v":1}""",
            },
            SigningCredentials = new SigningCredentials(ContextTokens.Key, SecurityAlgorithms.HmacSha256),
        });

        AssertUnsigned(await Verify(token));
    }

    // A token addressed to another callee is useless here.
    [Fact]
    public async Task Verify_AddressedToAnotherService_Unauthenticated()
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, audience: "gadget-service")));
    }

    // The audience is this service's own name, not whatever else the Bearer scheme accepts for access tokens.
    [Fact]
    public async Task Verify_AddressedToAnAudienceOnlyTheSchemeAccepts_Unauthenticated()
    {
        var http = ContextTokens.Request(configure: options =>
        {
            options.TokenValidationParameters.ValidAudiences = ["gadget-service"];
            options.TokenValidationParameters.ValidateAudience = false;
        });

        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, audience: "gadget-service"), http));
    }

    [Fact]
    public async Task Verify_FromAnotherIssuer_Unauthenticated()
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, issuer: "https://elsewhere.example.test")));
    }

    // An access token, or any other token signed by the same keys, is not a context token.
    [Theory]
    [InlineData("at+jwt")]
    [InlineData("JWT")]
    public async Task Verify_NotOfTheContextTokensType_Unauthenticated(string type)
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, type: type)));
    }

    // One taken from another caller's call is useless from this one.
    [Fact]
    public async Task Verify_IssuedToAnotherCaller_Unauthenticated()
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User), ContextTokens.Request(client: "internal.another-service")));
    }

    [Fact]
    public async Task Verify_IssuedToNoCaller_Unauthenticated()
    {
        var token = ContextTokens.Sign(
            user: User,
            claims: new Dictionary<string, object?> { [ContextToken.ClientClaim] = null });

        AssertUnsigned(await Verify(token));
    }

    // The match is exact, so a caller whose name differs only in case is another caller.
    [Fact]
    public async Task Verify_IssuedToACallerDifferingInCase_Unauthenticated()
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User), ContextTokens.Request(client: "internal.Gadget-service")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("delegated")]
    [InlineData("IMPERSONATED")]
    public async Task Verify_AnUnknownMode_Unauthenticated(string mode)
    {
        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, mode: mode)));
    }

    [Fact]
    public async Task Verify_NoMode_Unauthenticated()
    {
        var token = ContextTokens.Sign(
            user: User,
            claims: new Dictionary<string, object?> { [ContextToken.ModeClaim] = null });

        AssertUnsigned(await Verify(token));
    }

    public static TheoryData<string, string> OwnerClaims() => new()
    {
        { ContextToken.OrganizationClaim, "text" },
        { ContextToken.OrganizationClaim, "empty" },
        { ContextToken.UserClaim, "text" },
        { ContextToken.UserClaim, "empty" },
        { ContextToken.UserClaim, "number" },
        { ContextToken.UserClaim, "array" },
        { ContextToken.ProviderClaim, "text" },
        { ContextToken.ProviderClaim, "empty" },
    };

    // An owner is an identifier: not any other text, not the empty one, not a number or a list.
    [Theory]
    [MemberData(nameof(OwnerClaims))]
    public async Task Verify_AnOwnerThatIsNotAnIdentifier_Unauthenticated(string claim, string kind)
    {
        object owner = kind switch
        {
            "text" => "not-an-identifier",
            "empty" => Guid.Empty.ToString(),
            "number" => 42,
            _ => new[] { User.ToString() },
        };

        AssertUnsigned(await Verify(ContextTokens.Sign(claims: new Dictionary<string, object?> { [claim] = owner })));
    }

    // Signed, but its context object is not one: the caller's mistake, answered against the context header.
    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"v":2}""")]
    public async Task Verify_AMalformedContextObject_BadRequest(string context)
    {
        var token = ContextTokens.Sign(
            user: User,
            claims: new Dictionary<string, object?> { [ContextToken.ContextClaim] = context });

        AssertMalformed(await Verify(token), BitwardenHeaders.Context);
    }

    [Fact]
    public async Task Verify_NoContextObject_BadRequest()
    {
        var token = ContextTokens.Sign(
            user: User,
            claims: new Dictionary<string, object?> { [ContextToken.ContextClaim] = null });

        AssertMalformed(await Verify(token), BitwardenHeaders.Context);
    }

    // The plain headers stay the readable copy: present, they say what the token says.
    [Fact]
    public async Task Verify_PlainHeadersAgreeingWithTheToken_Accepted()
    {
        var verified = await Verify(ContextTokens.Sign(Organization, User, Provider), headers: new Dictionary<string, string>
        {
            [BitwardenHeaders.Organization] = Organization.ToString(),
            [BitwardenHeaders.User] = User.ToString().ToUpperInvariant(),
            [BitwardenHeaders.Provider] = Provider.ToString(),
            [BitwardenHeaders.Context] = """{"v":1}""",
        });

        Assert.Null(verified.Refusal);
        Assert.NotNull(verified.Context);
    }

    [Theory]
    [InlineData(BitwardenHeaders.Organization)]
    [InlineData(BitwardenHeaders.User)]
    [InlineData(BitwardenHeaders.Provider)]
    [InlineData(BitwardenHeaders.Context)]
    public async Task Verify_APlainHeaderDisagreeingWithTheToken_BadRequest(string header)
    {
        var value = header == BitwardenHeaders.Context
            ? """{"v":1,"deviceType":"9"}"""
            : new Guid("d4000000-0000-4000-8000-00000000000d").ToString();

        var verified = await Verify(
            ContextTokens.Sign(Organization, User, Provider),
            headers: new Dictionary<string, string> { [header] = value });

        AssertMalformed(verified, header);
    }

    // A header naming an owner the token names none of says something the token doesn't.
    [Theory]
    [InlineData(BitwardenHeaders.Organization)]
    [InlineData(BitwardenHeaders.User)]
    [InlineData(BitwardenHeaders.Provider)]
    public async Task Verify_APlainHeaderNamingAnOwnerTheTokenDoesnt_BadRequest(string header)
    {
        var verified = await Verify(
            ContextTokens.Sign(),
            headers: new Dictionary<string, string> { [header] = User.ToString() });

        AssertMalformed(verified, header);
    }

    public static TheoryData<string> Headers() => new(BitwardenHeaders.All);

    // A header sent twice could be read one way here and another way elsewhere, so it isn't read at all.
    [Theory]
    [MemberData(nameof(Headers))]
    public async Task Verify_AContextHeaderSentTwice_BadRequest(string header)
    {
        var http = ContextTokens.Request();
        var token = ContextTokens.Sign(Organization, User, Provider);
        http.Request.Headers[BitwardenHeaders.Delegation] = token;
        var value = header switch
        {
            BitwardenHeaders.Organization => Organization.ToString(),
            BitwardenHeaders.User => User.ToString(),
            BitwardenHeaders.Provider => Provider.ToString(),
            BitwardenHeaders.Context => """{"v":1}""",
            _ => token,
        };
        http.Request.Headers[header] = new Microsoft.Extensions.Primitives.StringValues([value, value]);

        AssertMalformed(await CallerDelegation.VerifyAsync(http), header);
    }

    // The published configuration's keys and issuer are trusted as the scheme's own, and binding the scheme from
    // configuration leaves the issuers an empty list rather than null.
    [Fact]
    public async Task Verify_KeysAndIssuerFromThePublishedConfiguration_Accepted()
    {
        var keys = new PublishedKeys(ContextTokens.Key);
        var http = ContextTokens.Request(configure: options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidIssuers = [] };
            options.ConfigurationManager = keys;
        });

        var verified = await Verify(ContextTokens.Sign(user: User), http);

        Assert.Equal(User, Assert.IsType<VerifiedContext>(verified.Context).User);
        Assert.Equal(0, keys.Refreshes);
    }

    // Identity rotated its key: one this service hasn't seen is looked up once, and the token accepted with it.
    [Fact]
    public async Task Verify_SignedWithARotatedKey_AcceptedAfterOneRefresh()
    {
        var rotated = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("rotated-test-signing-key-signs-nothing-real-either"))
        {
            KeyId = "rotated",
        };
        var keys = new RotatingKeys(rotated);
        var http = ContextTokens.Request(configure: options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = ContextTokens.Issuer };
            options.ConfigurationManager = keys;
        });

        var verified = await Verify(ContextTokens.Sign(user: User, key: rotated), http);

        Assert.Equal(User, Assert.IsType<VerifiedContext>(verified.Context).User);
        Assert.Equal(1, keys.Refreshes);
    }

    // Once, as the bearer handler does: a key that still isn't published is refused, not looked up again and again.
    [Fact]
    public async Task Verify_SignedWithAKeyNeverPublished_RefusedAfterOneRefresh()
    {
        var unknown = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("unknown-test-signing-key-signs-nothing-real-either"))
        {
            KeyId = "unknown",
        };
        var keys = new PublishedKeys(ContextTokens.Key);
        var http = ContextTokens.Request(configure: options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = ContextTokens.Issuer };
            options.ConfigurationManager = keys;
        });

        AssertUnsigned(await Verify(ContextTokens.Sign(user: User, key: unknown), http));
        Assert.Equal(1, keys.Refreshes);
    }

    // Identity publishes its next key before it signs with it: a token signed by either, while both are published, is
    // accepted without reading them again.
    [Fact]
    public async Task Verify_DuringAKeyRotationsOverlap_EitherKeyAcceptedWithoutARefresh()
    {
        var next = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("next-test-signing-key-signs-nothing-real-either-too"))
        {
            KeyId = "next",
        };
        var keys = new PublishedKeys(next, ContextTokens.Key);
        var http = ContextTokens.Request(configure: options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = ContextTokens.Issuer };
            options.ConfigurationManager = keys;
        });

        var byPrevious = await Verify(ContextTokens.Sign(user: User), http);
        var byNext = await Verify(ContextTokens.Sign(user: User, key: next), http);

        Assert.NotNull(byPrevious.Context);
        Assert.NotNull(byNext.Context);
        Assert.Equal(0, keys.Refreshes);
    }

    /// <summary>
    /// Identity's published keys, all of them from the first read.
    /// </summary>
    private sealed class PublishedKeys(params SecurityKey[] published) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Refreshes { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = ContextTokens.Issuer };
            foreach (var key in published)
            {
                configuration.SigningKeys.Add(key);
            }

            return Task.FromResult(configuration);
        }

        public void RequestRefresh() => Refreshes++;
    }

    /// <summary>
    /// Identity's published keys, which hold the rotated key only once a refresh is asked for.
    /// </summary>
    private sealed class RotatingKeys(SecurityKey rotated) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Refreshes { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var configuration = new OpenIdConnectConfiguration();
            if (Refreshes > 0)
            {
                configuration.SigningKeys.Add(rotated);
            }

            return Task.FromResult(configuration);
        }

        public void RequestRefresh() => Refreshes++;
    }
}
