using System.Security.Claims;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// Whether a caller is another service: an authenticated token issued to an <c>internal.{service}</c> client, and
/// nothing that only resembles one.
/// </summary>
public sealed class InternalCallerTests
{
    private static ClaimsPrincipal Caller(string? client, bool authenticated = true)
    {
        Claim[] claims = client is null ? [] : [new Claim(InternalCaller.ClientClaim, client)];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Bearer" : null));
    }

    [Fact]
    public void Constants_AreTheClientClaimAndItsPrefix()
    {
        Assert.Equal("client_id", InternalCaller.ClientClaim);
        Assert.Equal("internal.", InternalCaller.ClientPrefix);
    }

    [Fact]
    public void Is_AnInternalServicesToken_True()
    {
        Assert.True(InternalCaller.Is(Caller("internal.some-service")));
    }

    // The prefix alone names no service, and the match is exact in case.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("web")]
    [InlineData("internal.")]
    [InlineData("Internal.some-service")]
    [InlineData("organization.0c000000-0000-4000-8000-00000000000c")]
    public void Is_AnyOtherClient_False(string? client)
    {
        Assert.False(InternalCaller.Is(Caller(client)));
    }

    // A claim on a principal that never authenticated proves nothing.
    [Fact]
    public void Is_Unauthenticated_False()
    {
        Assert.False(InternalCaller.Is(Caller("internal.some-service", authenticated: false)));
    }

    [Fact]
    public void Is_NoPrincipal_False()
    {
        Assert.False(InternalCaller.Is(null));
    }
}
