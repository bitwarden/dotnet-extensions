using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// How an operation's declared authorization is settled from the operation, its class and the service, put on its
/// metadata, and how the declarations that depend on it refuse a shape they cannot honour.
/// </summary>
public sealed class OperationAuthorizationTests
{
    private const string Operation = "GetWidget";

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

    // Where the operation and its class both say something, the operation wins.
    [Fact]
    public void Settle_OperationAndClassBothDeclare_TheOperationWins()
    {
        var settled = OperationAuthorization.Settle(
            new AuthorizationAttribute
            {
                Organization = ContextRequirement.Forbidden,
                User = ContextRequirement.Required,
                Provider = ContextRequirement.Optional,
            },
            new AuthorizationAttribute
            {
                Organization = ContextRequirement.Optional,
                User = ContextRequirement.Forbidden,
                Provider = ContextRequirement.Required,
            },
            new AuthorizationDefaults(),
            reads: true);

        Assert.Equal(ContextRequirement.Forbidden, settled.Organization);
        Assert.Equal(ContextRequirement.Required, settled.User);
        Assert.Equal(ContextRequirement.Optional, settled.Provider);
    }

    // Unset is what lets a level say nothing: it falls through to the next, as no attribute at all does.
    [Fact]
    public void Settle_UnsetAtEveryLevel_IsTheServicesDefault()
    {
        var defaults = new AuthorizationDefaults
        {
            Organization = ContextRequirement.Optional,
            User = ContextRequirement.Required,
            Provider = ContextRequirement.Optional,
        };

        var settled = OperationAuthorization.Settle(new AuthorizationAttribute(), new AuthorizationAttribute(), defaults, reads: true);

        Assert.Equal(ContextRequirement.Optional, settled.Organization);
        Assert.Equal(ContextRequirement.Required, settled.User);
        Assert.Equal(ContextRequirement.Optional, settled.Provider);
    }

    [Fact]
    public void Settle_NothingDeclared_IsThePlatformsDefault()
    {
        var settled = OperationAuthorization.Settle(null, null, new AuthorizationDefaults(), reads: false);

        Assert.True(settled.Settled);
        Assert.Equal(ContextRequirement.Required, settled.Organization);
        Assert.Equal(ContextRequirement.Optional, settled.User);
        Assert.Equal(ContextRequirement.Forbidden, settled.Provider);
        Assert.Single(settled.GetRequirements());
    }

    [Theory]
    [InlineData("widgets.admin", "widgets.manage", true, "widgets.admin")]
    [InlineData(null, "widgets.manage", true, "widgets.manage")]
    [InlineData("", "widgets.manage", true, "widgets.manage")]
    [InlineData(null, null, true, "widgets.read")]
    [InlineData(null, null, false, "widgets.write")]
    [InlineData(null, "", false, "widgets.write")]
    public void Settle_Scope_OperationThenClassThenTheServicesForItsKind(
        string? declared, string? inherited, bool reads, string expected)
    {
        var settled = OperationAuthorization.Settle(
            new AuthorizationAttribute { Scope = declared },
            new AuthorizationAttribute { Scope = inherited },
            new AuthorizationDefaults { ReadScope = "widgets.read", WriteScope = "widgets.write" },
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

    [Fact]
    public void Settle_RolePermissionAndAnonymousOnTheOperation_AreKept()
    {
        var settled = OperationAuthorization.Settle(
            new AuthorizationAttribute
            {
                OrganizationRole = OrganizationRole.Admin,
                Permission = OrganizationPermission.ManageGroups,
                ProviderRole = ProviderRole.ServiceUser,
                Anonymous = true,
            },
            null,
            new AuthorizationDefaults(),
            reads: true);

        Assert.Equal(OrganizationRole.Admin, settled.OrganizationRole);
        Assert.Equal(OrganizationPermission.ManageGroups, settled.Permission);
        Assert.Equal(ProviderRole.ServiceUser, settled.ProviderRole);
        Assert.True(settled.Anonymous);
    }

    // --- Apply and SettledIn -----------------------------------------------------------------------------------------

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
        Assert.Equal(anonymous, metadata.OfType<AllowAnonymousAttribute>().Any());
    }

    // The declared attributes stay where an author wrote them; the settled one is added beside them.
    [Fact]
    public void Apply_KeepsTheDeclaredAttributes()
    {
        var declared = new AuthorizationAttribute { Scope = "widgets.read" };
        var metadata = new List<object> { declared };
        var settled = new AuthorizationAttribute { Scope = "widgets.read", Settled = true };

        OperationAuthorization.Apply(metadata, settled);

        Assert.Equal([declared, settled], metadata);
    }

    // Only a settled attribute demands anything, so only it is found.
    [Fact]
    public void SettledIn_FindsOnlyTheSettledAttribute()
    {
        var settled = new AuthorizationAttribute { Settled = true };
        var metadata = new List<object> { new AuthorizationAttribute(), "not an attribute", settled };

        Assert.Same(settled, Assert.Single(OperationAuthorization.SettledIn(metadata)));
    }

    // --- ApplyAnyScope -----------------------------------------------------------------------------------------------

    // Scopes only, with its provider optional or forbidden: the one requirement is replaced, not refused.
    [Theory]
    [InlineData(ContextRequirement.Optional, false)]
    [InlineData(ContextRequirement.Forbidden, true)]
    public void ApplyAnyScope_ScopesOnly_ReplacesTheRequirement(ContextRequirement provider, bool forbidsProvider)
    {
        var declared = new AuthorizationAttribute { Organization = ContextRequirement.Optional };
        var settled = Optional();
        settled.Provider = provider;
        var metadata = new List<object> { declared, settled };

        OperationAuthorization.ApplyAnyScope(Operation, new AnyScopeAttribute("widgets.read", "widgets.write"), metadata);

        Assert.DoesNotContain(settled, metadata);
        Assert.Contains(declared, metadata);
        var replaced = Assert.Single(metadata.OfType<AnyScopeRequirement>());
        Assert.Equal(["widgets.read", "widgets.write"], replaced.Scopes);
        Assert.Equal(forbidsProvider, replaced.ForbidsProvider);
    }

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
        var settled = Optional();
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

        var metadata = new List<object> { settled };

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            OperationAuthorization.ApplyAnyScope(Operation, new AnyScopeAttribute("widgets.read"), metadata));

        Assert.Contains($"'{Operation}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("checks scopes only", refusal.Message, StringComparison.Ordinal);
        Assert.Same(settled, Assert.Single(metadata));
    }

    [Fact]
    public void ApplyAnyScope_NamingNoScope_Refused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            OperationAuthorization.ApplyAnyScope(Operation, new AnyScopeAttribute(), new List<object> { Optional() }));

        Assert.Contains("names no scope", refusal.Message, StringComparison.Ordinal);
    }

    // Nothing settled to replace (or more than one): refused rather than leaving an operation open.
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ApplyAnyScope_NotExactlyOneSettled_Refused(int settled)
    {
        var metadata = new List<object> { new AuthorizationAttribute { Scope = "widgets.read" } };
        for (var i = 0; i < settled; i++)
        {
            metadata.Add(Optional());
        }

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            OperationAuthorization.ApplyAnyScope(Operation, new AnyScopeAttribute("widgets.read"), metadata));

        Assert.Contains("no settled authorization to replace", refusal.Message, StringComparison.Ordinal);
    }

    // --- CheckNamesOwner ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, "organizationId", typeof(Guid))]
    [InlineData(true, false, "body", typeof(NamedOrganization))]
    [InlineData(true, false, "body", typeof(DerivedNamedOrganization))]
    [InlineData(false, true, "providerId", typeof(Guid))]
    [InlineData(false, true, "body", typeof(NamedProvider))]
    public void CheckNamesOwner_NamedInThePathOrTheBody_Accepted(bool organization, bool provider, string parameter, Type type)
    {
        OperationAuthorization.CheckNamesOwner(
            Operation, organization, provider, [(parameter, type)], new List<object> { NamesOwner() });
    }

    [Fact]
    public void CheckNamesOwner_NamingBoth_Accepted()
    {
        OperationAuthorization.CheckNamesOwner(
            Operation,
            organization: true,
            provider: true,
            [("organizationId", typeof(Guid)), ("providerId", typeof(Guid))],
            new List<object> { NamesOwner() });
    }

    // The route value is matched exactly; a body naming the other kind of owner names nothing here.
    [Theory]
    [InlineData(true, false, "id", typeof(Guid), "{organizationId}")]
    [InlineData(true, false, "OrganizationId", typeof(Guid), "{organizationId}")]
    [InlineData(true, false, "body", typeof(NamedProvider), "{organizationId}")]
    [InlineData(false, true, "id", typeof(Guid), "{providerId}")]
    [InlineData(false, true, "body", typeof(NamedOrganization), "{providerId}")]
    public void CheckNamesOwner_NamedNowhere_Refused(bool organization, bool provider, string parameter, Type type, string expected)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            Operation, organization, provider, [(parameter, type)], new List<object> { NamesOwner() }));

        Assert.Contains($"'{Operation}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none-settled")]
    [InlineData("two-settled")]
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

        var metadata = kind switch
        {
            "none-settled" => new List<object>(),
            "two-settled" => new List<object> { settled, NamesOwner() },
            _ => new List<object> { settled },
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            Operation,
            organization: kind != "provider-optional",
            provider: kind == "provider-optional",
            [("organizationId", typeof(Guid)), ("providerId", typeof(Guid))],
            metadata));

        Assert.Contains("must require a person", refusal.Message, StringComparison.Ordinal);
    }

    // Naming only a provider, the session may still carry its organization; only the named kind is forbidden.
    [Fact]
    public void CheckNamesOwner_NamingAProviderWithTheOrganizationOptional_Accepted()
    {
        var settled = NamesOwner();
        settled.Organization = ContextRequirement.Optional;

        OperationAuthorization.CheckNamesOwner(
            Operation, organization: false, provider: true, [("providerId", typeof(Guid))], new List<object> { settled });
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
            Operation, organization: true, provider: false, [("organizationId", typeof(Guid))], new List<object> { settled }));

        Assert.Contains("may demand no role or permission", refusal.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Attribute> Incompatible() => new()
    {
        new AcceptsOrganizationCredentialAttribute("widgets.organization"),
        new AcceptsSendCredentialAttribute(),
        new CreatesOrganizationAttribute(),
    };

    [Theory]
    [MemberData(nameof(Incompatible))]
    public void CheckNamesOwner_AlsoAcceptingACredentialOrCreating_Refused(Attribute declaration)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckNamesOwner(
            Operation,
            organization: true,
            provider: false,
            [("organizationId", typeof(Guid))],
            new List<object> { NamesOwner(), declaration }));

        Assert.Contains("can't also accept", refusal.Message, StringComparison.Ordinal);
    }

    // --- CheckAcceptsSendCredential ----------------------------------------------------------------------------------

    [Fact]
    public void CheckAcceptsSendCredential_ScopedAndForbiddingUserAndOrganization_Accepted()
    {
        OperationAuthorization.CheckAcceptsSendCredential(Operation, new List<object> { SendOnly() });
    }

    [Theory]
    [InlineData("none-settled")]
    [InlineData("only-declared")]
    [InlineData("two-settled")]
    [InlineData("no-scope")]
    [InlineData("user-optional")]
    [InlineData("user-required")]
    [InlineData("organization-optional")]
    [InlineData("anonymous")]
    public void CheckAcceptsSendCredential_NotScopedToTheSendAlone_Refused(string kind)
    {
        var settled = SendOnly();
        switch (kind)
        {
            case "no-scope": settled.Scope = string.Empty; break;
            case "user-optional": settled.User = ContextRequirement.Optional; break;
            case "user-required": settled.User = ContextRequirement.Required; break;
            case "organization-optional": settled.Organization = ContextRequirement.Optional; break;
            case "anonymous": settled.Anonymous = true; break;
        }

        var metadata = kind switch
        {
            "none-settled" => new List<object>(),

            // The author's own attribute in the right shape demands nothing until it is settled.
            "only-declared" => new List<object>
            {
                new AuthorizationAttribute
                {
                    Scope = "widgets.send",
                    User = ContextRequirement.Forbidden,
                    Organization = ContextRequirement.Forbidden,
                },
            },
            "two-settled" => new List<object> { settled, SendOnly() },
            _ => new List<object> { settled },
        };

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            OperationAuthorization.CheckAcceptsSendCredential(Operation, metadata));

        Assert.Contains($"'{Operation}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("must demand a scope and forbid both", refusal.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Attribute> AlsoOrganizations() => new()
    {
        new AcceptsOrganizationCredentialAttribute("widgets.organization"),
        new NamesOrganizationAttribute(),
        new NamesProviderAttribute(),
    };

    [Theory]
    [MemberData(nameof(AlsoOrganizations))]
    public void CheckAcceptsSendCredential_AlsoAnOrganizations_Refused(Attribute declaration)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            OperationAuthorization.CheckAcceptsSendCredential(Operation, new List<object> { SendOnly(), declaration }));

        Assert.Contains("can't also accept an organization's", refusal.Message, StringComparison.Ordinal);
    }

    // --- CheckCreatesOrganization ------------------------------------------------------------------------------------

    [Fact]
    public void CheckCreatesOrganization_ForbiddingOneAndRequiringAPerson_Accepted()
    {
        OperationAuthorization.CheckCreatesOrganization(Operation, new List<object> { NamesOwner() });
    }

    [Theory]
    [InlineData("none-settled")]
    [InlineData("two-settled")]
    [InlineData("organization-optional")]
    [InlineData("organization-required")]
    [InlineData("user-optional")]
    [InlineData("user-forbidden")]
    [InlineData("anonymous")]
    public void CheckCreatesOrganization_OtherwiseDeclared_Refused(string kind)
    {
        var settled = NamesOwner();
        switch (kind)
        {
            case "organization-optional": settled.Organization = ContextRequirement.Optional; break;
            case "organization-required": settled.Organization = ContextRequirement.Required; break;
            case "user-optional": settled.User = ContextRequirement.Optional; break;
            case "user-forbidden": settled.User = ContextRequirement.Forbidden; break;
            case "anonymous": settled.Anonymous = true; break;
        }

        var metadata = kind switch
        {
            "none-settled" => new List<object>(),
            "two-settled" => new List<object> { settled, NamesOwner() },
            _ => new List<object> { settled },
        };

        var refusal = Assert.Throws<InvalidOperationException>(() => OperationAuthorization.CheckCreatesOrganization(Operation, metadata));

        Assert.Contains($"'{Operation}'", refusal.Message, StringComparison.Ordinal);
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
        Scope = "widgets.send",
        Organization = ContextRequirement.Forbidden,
        User = ContextRequirement.Forbidden,
        Provider = ContextRequirement.Forbidden,
        Settled = true,
    };

    private class NamedOrganization : INamesOrganization
    {
        public Guid OrganizationId { get; init; }
    }

    private sealed class DerivedNamedOrganization : NamedOrganization;

    private sealed class NamedProvider : INamesProvider
    {
        public Guid ProviderId { get; init; }
    }
}
