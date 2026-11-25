using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// An organization credential is a token naming an organization and no person, recognised only where the operation
/// being called accepts one.
/// </summary>
public sealed class OrganizationCredentialTests
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");
    private static readonly Guid Person = new("a1000000-0000-4000-8000-00000000000a");

    private static ClaimsPrincipal Token(string? organization, string? subject = null, bool authenticated = true)
    {
        var claims = new List<Claim>();

        if (organization is not null)
        {
            claims.Add(new Claim(CallerRole.OrganizationClaim, organization));
        }

        if (subject is not null)
        {
            claims.Add(new Claim(CurrentContext.SubjectClaim, subject));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Bearer" : null));
    }

    private static DefaultHttpContext Request(ClaimsPrincipal caller, bool accepted)
    {
        var http = new DefaultHttpContext { User = caller };
        object[] metadata = accepted ? [new AcceptsOrganizationCredentialAttribute("widgets.read")] : [];
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "GetWidget"));
        return http;
    }

    [Fact]
    public void Of_AnOrganizationAndNoPerson_IsTheOrganization()
    {
        Assert.Equal(Organization, OrganizationCredential.Of(Token(Organization.ToString())));
    }

    // A person's organization session carries both, and is a person's, whatever its sub says.
    [Theory]
    [InlineData("a1000000-0000-4000-8000-00000000000a")]
    [InlineData("not-a-person")]
    [InlineData("")]
    public void Of_ASubjectBesideTheOrganization_Null(string subject)
    {
        Assert.Null(OrganizationCredential.Of(Token(Organization.ToString(), subject)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-identifier")]
    public void Of_NoOrganization_Null(string? organization)
    {
        Assert.Null(OrganizationCredential.Of(Token(organization)));
    }

    [Fact]
    public void DeclaredBy_AnOperationAcceptingOne_IsItsDeclaration()
    {
        var declared = OrganizationCredential.DeclaredBy(Request(Token(null), accepted: true));

        Assert.Equal("widgets.read", Assert.IsType<AcceptsOrganizationCredentialAttribute>(declared).Scope);
    }

    [Fact]
    public void DeclaredBy_AnOperationAcceptingNone_Null()
    {
        Assert.Null(OrganizationCredential.DeclaredBy(Request(Token(null), accepted: false)));
        Assert.Null(OrganizationCredential.DeclaredBy(new DefaultHttpContext()));
    }

    [Fact]
    public void Accepted_ACredentialWhereAccepted_IsItsOrganization()
    {
        Assert.Equal(Organization, OrganizationCredential.Accepted(Request(Token(Organization.ToString()), accepted: true)));
    }

    [Fact]
    public void Accepted_ACredentialWhereNotAccepted_Null()
    {
        Assert.Null(OrganizationCredential.Accepted(Request(Token(Organization.ToString()), accepted: false)));
    }

    // Claims on a principal that never authenticated prove nothing.
    [Fact]
    public void Accepted_Unauthenticated_Null()
    {
        Assert.Null(OrganizationCredential.Accepted(Request(Token(Organization.ToString(), authenticated: false), accepted: true)));
    }

    [Fact]
    public void Accepted_APersonsSession_Null()
    {
        Assert.Null(OrganizationCredential.Accepted(Request(Token(Organization.ToString(), Person.ToString()), accepted: true)));
    }
}
