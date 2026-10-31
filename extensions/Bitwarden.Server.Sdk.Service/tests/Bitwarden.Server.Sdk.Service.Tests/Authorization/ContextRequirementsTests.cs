namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// The requirement carrying one operation's settled <see cref="AuthorizationAttribute"/> to its handler.
/// </summary>
public sealed class ContextRequirementsTests
{
    [Fact]
    public void Options_AreTheAttributeItCarries()
    {
        var options = new AuthorizationAttribute { Scope = "vault.read", Settled = true };

        Assert.Same(options, new ContextRequirements(options).Options);
    }

    // Two operations demanding the same are the same requirement; demanding something else, they are not.
    [Fact]
    public void Equality_FollowsWhatTheOptionsDemand()
    {
        var read = new ContextRequirements(new AuthorizationAttribute { Scope = "vault.read" });

        Assert.Equal(read, new ContextRequirements(new AuthorizationAttribute { Scope = "vault.read" }));
        Assert.NotEqual(read, new ContextRequirements(new AuthorizationAttribute { Scope = "vault.write" }));
    }
}
