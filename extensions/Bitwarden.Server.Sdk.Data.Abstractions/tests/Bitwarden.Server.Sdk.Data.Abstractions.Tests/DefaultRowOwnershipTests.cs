using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Data.Abstractions.Tests;

public sealed class DefaultRowOwnershipTests
{
    [Fact]
    public void RequireOrganization_WithOneInScope_ReturnsIt()
    {
        var expected = Guid.NewGuid();
        var ownership = new DefaultRowOwnership(new FixedContext { OrganizationId = expected });

        Assert.Equal(expected, ownership.RequireOrganization());
    }

    [Fact]
    public void RequireOrganization_WithNoneInScope_ThrowsForbiddenNamingTheMissingOwner()
    {
        var ownership = new DefaultRowOwnership(new FixedContext());

        var actual = Assert.Throws<ForbiddenException>(() => ownership.RequireOrganization());

        Assert.Contains("no organization is in scope", actual.Message);
    }

    [Fact]
    public void RequireUser_WithOneInScope_ReturnsIt()
    {
        var expected = Guid.NewGuid();
        var ownership = new DefaultRowOwnership(new FixedContext { UserId = expected });

        Assert.Equal(expected, ownership.RequireUser());
    }

    [Fact]
    public void RequireUser_WithNoneInScope_ThrowsForbidden()
    {
        var ownership = new DefaultRowOwnership(new FixedContext());

        Assert.Throws<ForbiddenException>(() => ownership.RequireUser());
    }

    [Fact]
    public void RequireProvider_WithOneInScope_ReturnsIt()
    {
        var expected = Guid.NewGuid();
        var ownership = new DefaultRowOwnership(new FixedContext { ProviderId = expected });

        Assert.Equal(expected, ownership.RequireProvider());
    }

    [Fact]
    public void RequireProvider_WithNoneInScope_ThrowsForbidden()
    {
        var ownership = new DefaultRowOwnership(new FixedContext());

        Assert.Throws<ForbiddenException>(() => ownership.RequireProvider());
    }

    [Fact]
    public void OptionalOwners_ReturnWhateverIsInScope()
    {
        var context = new FixedContext { OrganizationId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var ownership = new DefaultRowOwnership(context);

        Assert.Equal(context.OrganizationId, ownership.Organization());
        Assert.Equal(context.UserId, ownership.User());
        Assert.Null(ownership.Provider());
    }

    private sealed class FixedContext : IContext
    {
        public Guid? OrganizationId { get; init; }

        public Guid? UserId { get; init; }

        public Guid? ProviderId { get; init; }
    }
}
