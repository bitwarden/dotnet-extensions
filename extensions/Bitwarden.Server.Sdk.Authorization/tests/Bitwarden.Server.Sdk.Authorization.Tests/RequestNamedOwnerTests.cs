using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// The organization or provider a product service's operation names, in its path or its body, answered only where the
/// operation is declared to name one and the session names none.
/// </summary>
public sealed class RequestNamedOwnerTests
{
    private static readonly Guid FromPath = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid FromBody = new("b0000000-0000-4000-8000-0000000000bb");
    private static readonly Guid FromSession = new("c0000000-0000-4000-8000-0000000000cc");

    private static DefaultHttpContext Request(params object[] metadata)
    {
        var http = new DefaultHttpContext();
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "CreateWidget"));
        return http;
    }

    private static RequestNamedOwner Owner(HttpContext http, StatedContext? session = null) =>
        new(new HttpContextAccessor { HttpContext = http }, session ?? new StatedContext());

    [Fact]
    public void OrganizationId_NamedInThePath_IsThePaths()
    {
        var http = Request(new NamesOrganizationAttribute());
        http.Request.RouteValues[NamesOrganizationAttribute.RouteValue] = FromPath.ToString();

        Assert.Equal(FromPath, Owner(http).OrganizationId);
    }

    [Fact]
    public void OrganizationId_NamedInTheBody_IsTheBodys()
    {
        var http = Request(new NamesOrganizationAttribute());
        RequestNamedOwners.NoteOrganization(http, FromBody);

        Assert.Equal(FromBody, Owner(http).OrganizationId);
    }

    [Fact]
    public void OrganizationId_NamedInBoth_IsThePaths()
    {
        var http = Request(new NamesOrganizationAttribute());
        http.Request.RouteValues[NamesOrganizationAttribute.RouteValue] = FromPath.ToString();
        RequestNamedOwners.NoteOrganization(http, FromBody);

        Assert.Equal(FromPath, Owner(http).OrganizationId);
    }

    // A path value that isn't one names nothing, and the body's is read instead.
    [Theory]
    [InlineData("not-an-identifier")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void OrganizationId_APathValueThatIsNotAnOwner_IsTheBodys(string path)
    {
        var http = Request(new NamesOrganizationAttribute());
        http.Request.RouteValues[NamesOrganizationAttribute.RouteValue] = path;
        RequestNamedOwners.NoteOrganization(http, FromBody);

        Assert.Equal(FromBody, Owner(http).OrganizationId);
    }

    [Fact]
    public void OrganizationId_TheEmptyIdentifierInTheBody_Null()
    {
        var http = Request(new NamesOrganizationAttribute());
        RequestNamedOwners.NoteOrganization(http, Guid.Empty);

        Assert.Null(Owner(http).OrganizationId);
    }

    // Undeclared, an operation's path value is just a value: it names no owner to authorize against.
    [Fact]
    public void OrganizationId_OperationNotDeclaredToNameOne_Null()
    {
        var http = Request();
        http.Request.RouteValues[NamesOrganizationAttribute.RouteValue] = FromPath.ToString();
        RequestNamedOwners.NoteOrganization(http, FromBody);

        Assert.Null(Owner(http).OrganizationId);
    }

    // The session's own organization is the one authorized against; the request has no say beside it.
    [Fact]
    public void OrganizationId_SessionNamesOne_Null()
    {
        var http = Request(new NamesOrganizationAttribute());
        http.Request.RouteValues[NamesOrganizationAttribute.RouteValue] = FromPath.ToString();

        Assert.Null(Owner(http, new StatedContext { OrganizationId = FromSession }).OrganizationId);
    }

    [Fact]
    public void OrganizationId_NoRequest_Null()
    {
        Assert.Null(new RequestNamedOwner(new HttpContextAccessor(), new StatedContext()).OrganizationId);
    }

    [Fact]
    public void ProviderId_NamedInThePath_IsThePaths()
    {
        var http = Request(new NamesProviderAttribute());
        http.Request.RouteValues[NamesProviderAttribute.RouteValue] = FromPath.ToString();

        Assert.Equal(FromPath, Owner(http).ProviderId);
    }

    [Fact]
    public void ProviderId_NamedInTheBody_IsTheBodys()
    {
        var http = Request(new NamesProviderAttribute());
        RequestNamedOwners.NoteProvider(http, FromBody);

        Assert.Equal(FromBody, Owner(http).ProviderId);
    }

    [Fact]
    public void ProviderId_OperationNotDeclaredToNameOne_Null()
    {
        var http = Request(new NamesOrganizationAttribute());
        http.Request.RouteValues[NamesProviderAttribute.RouteValue] = FromPath.ToString();
        RequestNamedOwners.NoteProvider(http, FromBody);

        Assert.Null(Owner(http).ProviderId);
    }

    [Fact]
    public void ProviderId_SessionNamesOne_Null()
    {
        var http = Request(new NamesProviderAttribute());
        RequestNamedOwners.NoteProvider(http, FromBody);

        Assert.Null(Owner(http, new StatedContext { ProviderId = FromSession }).ProviderId);
    }

    // Each kind of owner reads its own route value and its own note, never the other's.
    [Fact]
    public void Owners_DeclaredBoth_EachReadsItsOwn()
    {
        var http = Request(new NamesOrganizationAttribute(), new NamesProviderAttribute());
        RequestNamedOwners.NoteOrganization(http, FromBody);
        http.Request.RouteValues[NamesProviderAttribute.RouteValue] = FromPath.ToString();

        var owner = Owner(http);

        Assert.Equal(FromBody, owner.OrganizationId);
        Assert.Equal(FromPath, owner.ProviderId);
    }
}
