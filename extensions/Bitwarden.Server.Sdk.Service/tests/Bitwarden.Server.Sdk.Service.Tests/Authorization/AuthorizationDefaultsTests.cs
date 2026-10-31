namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// What a service's operations demand unless the service says otherwise.
/// </summary>
public sealed class AuthorizationDefaultsTests
{
    // An organization required, a person optional, a provider forbidden, and no scope a service didn't name.
    [Fact]
    public void Defaults_AreThePlatforms()
    {
        var defaults = new AuthorizationDefaults();

        Assert.Equal(ContextRequirement.Required, defaults.Organization);
        Assert.Equal(ContextRequirement.Optional, defaults.User);
        Assert.Equal(ContextRequirement.Forbidden, defaults.Provider);
        Assert.Null(defaults.ReadScope);
        Assert.Null(defaults.WriteScope);
    }

    // A service changes only what it names; everything else keeps the platform's default.
    [Fact]
    public void With_ChangesOnlyWhatItNames()
    {
        var defaults = new AuthorizationDefaults() with { Organization = ContextRequirement.Optional, ReadScope = "vault.read" };

        Assert.Equal(
            new AuthorizationDefaults
            {
                Organization = ContextRequirement.Optional,
                User = ContextRequirement.Optional,
                Provider = ContextRequirement.Forbidden,
                ReadScope = "vault.read",
            },
            defaults);
        Assert.NotEqual(new AuthorizationDefaults(), defaults);
    }
}
