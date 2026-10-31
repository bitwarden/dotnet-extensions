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

    [Fact]
    public void ApplyAnyScope_NamingNoScope_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.ApplyAnyScope(
            "probe", new AnyScopeAttribute(), new List<object> { Optional() }));

        Assert.Contains("names no scope", refusal.Message, StringComparison.Ordinal);
    }

    // Nothing settled to replace (or more than one): refused rather than leaving an operation open.
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ApplyAnyScope_NotExactlyOneSettled_Refused(int settled)
    {
        var metadata = new List<object> { new AuthorizationAttribute { Scope = SessionHost.Scope } };
        for (var i = 0; i < settled; i++)
        {
            metadata.Add(Optional());
        }

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.ApplyAnyScope(
            "probe", new AnyScopeAttribute(SessionHost.Scope), metadata));

        Assert.Contains("no settled authorization to replace", refusal.Message, StringComparison.Ordinal);
    }

    // --- Settle ------------------------------------------------------------------------------------------------------

    // The operation, then its class, then the service, each member on its own.
    [Fact]
    public void Settle_MostSpecificWinsMemberByMember()
    {
        var settled = OperationAuthorization.Settle(
            new AuthorizationAttribute { User = ContextRequirement.Required },
            new AuthorizationAttribute { User = ContextRequirement.Forbidden, Organization = ContextRequirement.Optional },
            new AuthorizationDefaults(),
            reads: true);

        Assert.True(settled.Settled);
        Assert.Equal(ContextRequirement.Required, settled.User);
        Assert.Equal(ContextRequirement.Optional, settled.Organization);
        Assert.Equal(ContextRequirement.Forbidden, settled.Provider);
    }

    [Theory]
    [InlineData("operation", "class", true, "operation")]
    [InlineData(null, "class", true, "class")]
    [InlineData("", "class", true, "class")]
    [InlineData(null, null, true, "read")]
    [InlineData(null, null, false, "write")]
    [InlineData(null, "", false, "write")]
    public void Settle_Scope_OperationThenClassThenTheServicesForItsKind(string? declared, string? inherited, bool reads, string expected)
    {
        var settled = OperationAuthorization.Settle(
            new AuthorizationAttribute { Scope = declared },
            new AuthorizationAttribute { Scope = inherited },
            new AuthorizationDefaults { ReadScope = "read", WriteScope = "write" },
            reads);

        Assert.Equal(expected, settled.Scope);
    }

    // A service naming no scope settles an empty one, which demands none.
    [Fact]
    public void Settle_NoScopeAnywhere_IsEmpty()
    {
        Assert.Equal(string.Empty, OperationAuthorization.Settle(null, null, new AuthorizationDefaults(), reads: true).Scope);
    }

    // A role, a permission and Anonymous are the operation's alone: a class setting them sets nothing.
    [Fact]
    public void Settle_RolePermissionAndAnonymous_AreNeverInherited()
    {
        var settled = OperationAuthorization.Settle(
            null,
            new AuthorizationAttribute
            {
                OrganizationRole = OrganizationRole.Owner,
                Permission = OrganizationPermission.ManageUsers,
                ProviderRole = ProviderRole.ProviderAdmin,
                Anonymous = true,
            },
            new AuthorizationDefaults(),
            reads: true);

        Assert.Equal(OrganizationRole.None, settled.OrganizationRole);
        Assert.Equal(OrganizationPermission.None, settled.Permission);
        Assert.Equal(ProviderRole.None, settled.ProviderRole);
        Assert.False(settled.Anonymous);
    }

    // --- Apply ---------------------------------------------------------------------------------------------------

    // An anonymous operation also says so to the framework, which otherwise challenges before any requirement runs.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_Anonymous_AllowsAnonymousToo(bool anonymous)
    {
        var metadata = new List<object>();
        var settled = new AuthorizationAttribute { Anonymous = anonymous, Settled = true };

        OperationAuthorization.Apply(metadata, settled);

        Assert.Same(settled, metadata[0]);
        Assert.Equal(anonymous, metadata.OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any());
    }

    // --- CheckNamesOwner ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, "organizationId", typeof(Guid))]
    [InlineData(true, false, "body", typeof(NamedOrganization))]
    [InlineData(false, true, "providerId", typeof(Guid))]
    [InlineData(false, true, "body", typeof(NamedProvider))]
    public void CheckNamesOwner_NamedInThePathOrTheBody_Accepted(bool organization, bool provider, string parameter, Type type)
    {
        OperationAuthorization.CheckNamesOwner(
            "probe", organization, provider, [(parameter, type)], new List<object> { NamesOwner() });
    }

    [Theory]
    [InlineData(true, false, "{organizationId}")]
    [InlineData(false, true, "{providerId}")]
    public void CheckNamesOwner_NamedNowhere_Refused(bool organization, bool provider, string expected)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            "probe", organization, provider, [("id", typeof(Guid))], new List<object> { NamesOwner() }));

        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none-settled")]
    [InlineData("anonymous")]
    [InlineData("user-optional")]
    [InlineData("organization-optional")]
    [InlineData("provider-optional")]
    public void CheckNamesOwner_NotRequiringAPersonAndForbiddingTheSessionsOwn_Refused(string kind)
    {
        var settled = NamesOwner();
        switch (kind)
        {
            case "anonymous": settled.Anonymous = true; break;
            case "user-optional": settled.User = ContextRequirement.Optional; break;
            case "organization-optional": settled.Organization = ContextRequirement.Optional; break;
            case "provider-optional": settled.Provider = ContextRequirement.Optional; break;
        }

        var metadata = kind == "none-settled" ? new List<object>() : new List<object> { settled };

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            "probe",
            organization: kind != "provider-optional",
            provider: kind == "provider-optional",
            [("organizationId", typeof(Guid)), ("providerId", typeof(Guid))],
            metadata));

        Assert.Contains("must require a person", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("organization-role")]
    [InlineData("permission")]
    [InlineData("provider-role")]
    public void CheckNamesOwner_DemandingARoleOrPermission_Refused(string kind)
    {
        var settled = NamesOwner();
        switch (kind)
        {
            case "organization-role": settled.OrganizationRole = OrganizationRole.User; break;
            case "permission": settled.Permission = OrganizationPermission.ManageUsers; break;
            default: settled.ProviderRole = ProviderRole.ServiceUser; break;
        }

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            "probe", organization: true, provider: false, [("organizationId", typeof(Guid))], new List<object> { settled }));

        Assert.Contains("may demand no role or permission", refusal.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Attribute> Incompatible() => new()
    {
        new AcceptsOrganizationCredentialAttribute("api.organization"),
        new AcceptsSendCredentialAttribute(),
        new CreatesOrganizationAttribute(),
    };

    [Theory]
    [MemberData(nameof(Incompatible))]
    public void CheckNamesOwner_AlsoAcceptingACredentialOrCreating_Refused(Attribute declaration)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            "probe", organization: true, provider: false, [("organizationId", typeof(Guid))], new List<object> { NamesOwner(), declaration }));

        Assert.Contains("can't also accept", refusal.Message, StringComparison.Ordinal);
    }

    // --- CheckAcceptsSendCredential ------------------------------------------------------------------------------------

    [Fact]
    public void CheckAcceptsSendCredential_ScopedAndForbiddingUserAndOrganization_Accepted()
    {
        OperationAuthorization.CheckAcceptsSendCredential("probe", new List<object> { SendOnly() });
    }

    [Theory]
    [InlineData("none-settled")]
    [InlineData("no-scope")]
    [InlineData("user-optional")]
    [InlineData("organization-optional")]
    [InlineData("anonymous")]
    public void CheckAcceptsSendCredential_NotScopedToTheSendAlone_Refused(string kind)
    {
        var settled = SendOnly();
        switch (kind)
        {
            case "no-scope": settled.Scope = string.Empty; break;
            case "user-optional": settled.User = ContextRequirement.Optional; break;
            case "organization-optional": settled.Organization = ContextRequirement.Optional; break;
            case "anonymous": settled.Anonymous = true; break;
        }

        var metadata = kind == "none-settled" ? new List<object>() : new List<object> { settled };

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckAcceptsSendCredential("probe", metadata));

        Assert.Contains("must demand a scope and forbid both", refusal.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Attribute> AlsoOrganizations() => new()
    {
        new AcceptsOrganizationCredentialAttribute("api.organization"),
        new NamesOrganizationAttribute(),
        new NamesProviderAttribute(),
    };

    [Theory]
    [MemberData(nameof(AlsoOrganizations))]
    public void CheckAcceptsSendCredential_AlsoAnOrganizations_Refused(Attribute declaration)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            OperationAuthorization.CheckAcceptsSendCredential("probe", new List<object> { SendOnly(), declaration }));

        Assert.Contains("can't also accept an organization's", refusal.Message, StringComparison.Ordinal);
    }

    // --- CheckCreatesOrganization --------------------------------------------------------------------------------------

    [Fact]
    public void CheckCreatesOrganization_ForbiddingOneAndRequiringAPerson_Accepted()
    {
        OperationAuthorization.CheckCreatesOrganization("probe", new List<object> { NamesOwner() });
    }

    [Theory]
    [InlineData("none-settled")]
    [InlineData("organization-optional")]
    [InlineData("user-optional")]
    [InlineData("anonymous")]
    public void CheckCreatesOrganization_OtherwiseDeclared_Refused(string kind)
    {
        var settled = NamesOwner();
        switch (kind)
        {
            case "organization-optional": settled.Organization = ContextRequirement.Optional; break;
            case "user-optional": settled.User = ContextRequirement.Optional; break;
            case "anonymous": settled.Anonymous = true; break;
        }

        var metadata = kind == "none-settled" ? new List<object>() : new List<object> { settled };

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckCreatesOrganization("probe", metadata));

        Assert.Contains("creates an organization", refusal.Message, StringComparison.Ordinal);
    }

    private static AuthorizationAttribute Optional() => new()
    {
        Organization = ContextRequirement.Optional,
        User = ContextRequirement.Optional,
        Provider = ContextRequirement.Forbidden,
        Settled = true,
    };

    /// <summary>
    /// What an operation naming an organization or provider, or creating one, must settle: a person, and neither
    /// named by the session.
    /// </summary>
    private static AuthorizationAttribute NamesOwner() => new()
    {
        Organization = ContextRequirement.Forbidden,
        User = ContextRequirement.Required,
        Provider = ContextRequirement.Forbidden,
        Settled = true,
    };

    private static AuthorizationAttribute SendOnly() => new()
    {
        Scope = SessionHost.SendScope,
        Organization = ContextRequirement.Forbidden,
        User = ContextRequirement.Forbidden,
        Provider = ContextRequirement.Forbidden,
        Settled = true,
    };
}
