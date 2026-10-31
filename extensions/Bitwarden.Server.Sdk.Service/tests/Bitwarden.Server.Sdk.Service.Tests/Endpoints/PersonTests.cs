using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A handler's <see cref="Person"/> is the user the request's caller context names, and a request
/// naming nobody is refused rather than bound to an empty identifier.
/// </summary>
public sealed class PersonTests
{
    private static readonly Guid User = new("b2000000-0000-4000-8000-00000000000b");

    [Fact]
    public async Task BindAsync_ContextNamesAUser_IsThatUser()
    {
        var actual = await Person.BindAsync(Request(User));

        Assert.Equal(User, actual.Id);
    }

    [Fact]
    public async Task BindAsync_ContextNamesNobody_Forbidden()
    {
        var actual = await Assert.ThrowsAsync<ForbiddenException>(async () => await Person.BindAsync(Request(null)));

        Assert.Equal("The person is required.", actual.Message);
    }

    [Fact]
    public async Task BindAsync_NoRequest_Refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await Person.BindAsync(null!));
    }

    [Fact]
    public void ImplicitGuid_IsTheIdentifier()
    {
        Guid actual = new Person(User);

        Assert.Equal(User, actual);
    }

    private static DefaultHttpContext Request(Guid? user) => new()
    {
        RequestServices = new ServiceCollection()
            .AddSingleton<IContext>(new Caller(user))
            .BuildServiceProvider(),
    };

    private sealed class Caller(Guid? user) : IContext
    {
        public Guid? OrganizationId => null;

        public Guid? UserId => user;

        public Guid? ProviderId => null;
    }
}
