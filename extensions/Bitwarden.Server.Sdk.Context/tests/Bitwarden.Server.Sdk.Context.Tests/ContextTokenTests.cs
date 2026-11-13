namespace Bitwarden.Server.Sdk.Context.Tests;

public sealed class ContextTokenTests
{
    [Fact]
    public void Type_IsNeverAnAccessToken()
    {
        Assert.Equal("bitwarden-context+jwt", ContextToken.Type);
        Assert.NotEqual("at+jwt", ContextToken.Type);
    }

    [Fact]
    public void TokenTypes_AreTheContract()
    {
        Assert.Equal("urn:bitwarden:params:oauth:token-type:caller-context", ContextToken.TokenType);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", ContextToken.AccessTokenType);
    }

    [Fact]
    public void Claims_AreTheContract()
    {
        Assert.Equal("context_organization", ContextToken.OrganizationClaim);
        Assert.Equal("context_user", ContextToken.UserClaim);
        Assert.Equal("context_provider", ContextToken.ProviderClaim);
        Assert.Equal("context", ContextToken.ContextClaim);
        Assert.Equal("ctx_mode", ContextToken.ModeClaim);
        Assert.Equal("client_id", ContextToken.ClientClaim);
        Assert.Equal("act", ContextToken.ActorClaim);
    }

    [Fact]
    public void Modes_AreDistinct()
    {
        Assert.Equal("DELEGATED", ContextToken.Delegated);
        Assert.Equal("ASSERTED", ContextToken.Asserted);
    }
}
