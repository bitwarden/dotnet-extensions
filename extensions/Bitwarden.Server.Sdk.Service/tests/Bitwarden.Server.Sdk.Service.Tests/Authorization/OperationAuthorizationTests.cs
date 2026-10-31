using Bitwarden.Server.Sdk.Service.Tests.TestHost;

namespace Bitwarden.Server.Sdk.Service.Tests.Authorization;

/// <summary>
/// How an operation's declared authorization is settled at startup.
/// </summary>
public sealed class OperationAuthorizationTests
{
    // It checks scopes only, so an operation also demanding anything else is refused at startup.
    [Theory]
    [InlineData("organization")]
    [InlineData("user")]
    [InlineData("provider")]
    [InlineData("organization-role")]
    [InlineData("permission")]
    [InlineData("provider-role")]
    [InlineData("anonymous")]
    public void ApplyAnyScope_CombinedWithAnotherRequirement_Refused(string requirement)
    {
        var settled = new AuthorizationAttribute
        {
            Organization = ContextRequirement.Optional,
            User = ContextRequirement.Optional,
            Provider = ContextRequirement.Forbidden,
            Settled = true,
        };
        switch (requirement)
        {
            case "organization": settled.Organization = ContextRequirement.Required; break;
            case "user": settled.User = ContextRequirement.Forbidden; break;
            case "provider": settled.Provider = ContextRequirement.Required; break;
            case "organization-role": settled.OrganizationRole = OrganizationRole.Admin; break;
            case "permission": settled.Permission = OrganizationPermission.ManageUsers; break;
            case "provider-role": settled.ProviderRole = ProviderRole.ServiceUser; break;
            default: settled.Anonymous = true; break;
        }

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.ApplyAnyScope(
            "probe", new AnyScopeAttribute(SessionHost.Scope, SessionHost.OtherScope), new List<object> { settled }));

        Assert.Contains("checks scopes only", refusal.Message, StringComparison.Ordinal);
    }

    // Scopes only, with its provider optional or forbidden: the one requirement is replaced, not refused.
    [Theory]
    [InlineData(ContextRequirement.Optional, false)]
    [InlineData(ContextRequirement.Forbidden, true)]
    public void ApplyAnyScope_ScopesOnly_ReplacesTheRequirement(ContextRequirement provider, bool forbidsProvider)
    {
        var settled = new AuthorizationAttribute
        {
            Organization = ContextRequirement.Optional,
            User = ContextRequirement.Optional,
            Provider = provider,
            Settled = true,
        };
        var metadata = new List<object> { settled };

        OperationAuthorization.ApplyAnyScope("probe", new AnyScopeAttribute(SessionHost.Scope, SessionHost.OtherScope), metadata);

        var replaced = Assert.IsType<AnyScopeRequirement>(Assert.Single(metadata));
        Assert.Equal(new[] { SessionHost.Scope, SessionHost.OtherScope }, replaced.Scopes);
        Assert.Equal(forbidsProvider, replaced.ForbidsProvider);
    }
}
