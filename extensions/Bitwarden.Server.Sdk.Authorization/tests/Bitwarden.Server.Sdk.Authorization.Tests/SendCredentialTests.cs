using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// A Send credential names the Send it was minted for, and is recognised only where the operation declares it accepts
/// one.
/// </summary>
public sealed class SendCredentialTests
{
    private static ClaimsPrincipal Token(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    [Fact]
    public void SendId_ATokenNamingASend_IsTheSend()
    {
        Assert.Equal("a-send-id", SendCredential.SendId(Token(new Claim(SendCredential.Claim, "a-send-id"))));
    }

    [Fact]
    public void SendId_ATokenNamingNone_Null()
    {
        Assert.Null(SendCredential.SendId(Token(new Claim(CurrentContext.SubjectClaim, "a1000000-0000-4000-8000-00000000000a"))));
    }

    [Fact]
    public void SendId_AnEmptyClaim_Null()
    {
        Assert.Null(SendCredential.SendId(Token(new Claim(SendCredential.Claim, string.Empty))));
    }

    [Fact]
    public void DeclaredBy_AnOperationAcceptingOne_True()
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AcceptsSendCredentialAttribute()), "GetWidget"));

        Assert.True(SendCredential.DeclaredBy(http));
    }

    [Fact]
    public void DeclaredBy_AnOperationAcceptingNone_False()
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "GetWidget"));

        Assert.False(SendCredential.DeclaredBy(http));
        Assert.False(SendCredential.DeclaredBy(new DefaultHttpContext()));
    }
}
