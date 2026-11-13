using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// Notes the organization or provider a request's body names, each apart, for the rest of the request.
/// </summary>
public sealed class RequestNamedOwnersTests
{
    private static readonly Guid First = new("a0000000-0000-4000-8000-0000000000aa");
    private static readonly Guid Second = new("b0000000-0000-4000-8000-0000000000bb");

    [Fact]
    public void NoteOrganization_IsKeptForTheRequest_AndNamesNoProvider()
    {
        var http = new DefaultHttpContext();

        RequestNamedOwners.NoteOrganization(http, First);

        Assert.Equal(First, http.Items[RequestNamedOwners.OrganizationKey]);
        Assert.False(http.Items.ContainsKey(RequestNamedOwners.ProviderKey));
    }

    [Fact]
    public void NoteProvider_IsKeptForTheRequest_AndNamesNoOrganization()
    {
        var http = new DefaultHttpContext();

        RequestNamedOwners.NoteProvider(http, First);

        Assert.Equal(First, http.Items[RequestNamedOwners.ProviderKey]);
        Assert.False(http.Items.ContainsKey(RequestNamedOwners.OrganizationKey));
    }

    // The body is bound once; a second note is what it last said.
    [Fact]
    public void Note_Twice_TheLastIsKept()
    {
        var http = new DefaultHttpContext();

        RequestNamedOwners.NoteOrganization(http, First);
        RequestNamedOwners.NoteOrganization(http, Second);

        Assert.Equal(Second, http.Items[RequestNamedOwners.OrganizationKey]);
    }

    // A request's notes are its own.
    [Fact]
    public void Note_IsNotSeenByAnotherRequest()
    {
        RequestNamedOwners.NoteOrganization(new DefaultHttpContext(), First);

        Assert.False(new DefaultHttpContext().Items.ContainsKey(RequestNamedOwners.OrganizationKey));
    }
}
