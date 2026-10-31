namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class CallerContextTokenTests
{
    [Fact]
    public void Type_IsNeverAnAccessToken()
    {
        Assert.Equal("bitwarden-context+jwt", CallerContextToken.Type);
        Assert.NotEqual("at+jwt", CallerContextToken.Type);
    }

    [Fact]
    public void TokenTypes_AreTheContract()
    {
        Assert.Equal("urn:bitwarden:params:oauth:token-type:caller-context", CallerContextToken.TokenType);
        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", CallerContextToken.AccessTokenType);
    }

    [Fact]
    public void Claims_AreTheContract()
    {
        Assert.Equal("context_organization", CallerContextToken.OrganizationClaim);
        Assert.Equal("context_user", CallerContextToken.UserClaim);
        Assert.Equal("context_provider", CallerContextToken.ProviderClaim);
        Assert.Equal("context", CallerContextToken.ContextClaim);
        Assert.Equal("ctx_mode", CallerContextToken.ModeClaim);
        Assert.Equal("client_id", CallerContextToken.ClientClaim);
        Assert.Equal("act", CallerContextToken.ActorClaim);
    }

    [Fact]
    public void Modes_AreDistinct()
    {
        Assert.Equal("DELEGATED", CallerContextToken.Delegated);
        Assert.Equal("ASSERTED", CallerContextToken.Asserted);
    }

    [Fact]
    public void Parameters_MatchTheClaims()
    {
        Assert.Equal(CallerContextToken.OrganizationClaim, CallerContextToken.Parameters.Organization);
        Assert.Equal(CallerContextToken.UserClaim, CallerContextToken.Parameters.User);
        Assert.Equal(CallerContextToken.ProviderClaim, CallerContextToken.Parameters.Provider);
        Assert.Equal(CallerContextToken.ContextClaim, CallerContextToken.Parameters.Context);
    }
}
