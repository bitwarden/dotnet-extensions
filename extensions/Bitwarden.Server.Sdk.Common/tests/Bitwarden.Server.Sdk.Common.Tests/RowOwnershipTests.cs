using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bitwarden.Server.Sdk.Common.Tests;

// Refusing when there is no owner in scope is the single behaviour this package exists for.
// Everything else it does is plumbing around this decision.
public sealed class RowOwnershipTests
{
    [Fact]
    public void RequireOrganization_WithOneInScope_ReturnsIt()
    {
        // Arrange
        var expected = Guid.NewGuid();
        var ownership = new DefaultRowOwnership(new Context { OrganizationId = expected });

        // Act
        var actual = ownership.RequireOrganization();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RequireOrganization_WithNoneInScope_IsRefused()
    {
        // Arrange
        var ownership = new DefaultRowOwnership(new Context());

        // Act / Assert
        Assert.Throws<ForbiddenException>(() => ownership.RequireOrganization());
    }

    [Fact]
    public void RequireUser_WithOneInScope_ReturnsIt()
    {
        // Arrange
        var expected = Guid.NewGuid();
        var ownership = new DefaultRowOwnership(new Context { UserId = expected });

        // Act
        var actual = ownership.RequireUser();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RequireUser_WithNoneInScope_IsRefused()
    {
        // Arrange
        var ownership = new DefaultRowOwnership(new Context());

        // Act / Assert
        Assert.Throws<ForbiddenException>(() => ownership.RequireUser());
    }

    // Refused, never widened: the exception must not be something a caller could mistake for
    // "there are no rows".
    [Fact]
    public void Refusal_SaysWhyRatherThanReturningNothing()
    {
        // Arrange
        var ownership = new DefaultRowOwnership(new Context());

        // Act
        var actual = Assert.Throws<ForbiddenException>(() => ownership.RequireOrganization());

        // Assert
        Assert.Contains("no organization is in scope", actual.Message);
    }

    private sealed class Context : IContext
    {
        public Guid? OrganizationId { get; init; }

        public Guid? UserId { get; init; }

        public Guid? ProviderId { get; init; }
    }

    // The platform registers the default with TryAdd; a service that wants its own registers it
    // outright from ConfigureServices, which runs afterwards. Last registration wins, so the
    // service's is the one resolved. Pinned because the whole override story rests on it.
    [Fact]
    public void Registration_WhenAServiceRegistersItsOwn_TheServiceWins()
    {
        // Arrange
        var services = new ServiceCollection();
        services.TryAddSingleton<IContext>(new Context());
        services.TryAddSingleton<IRowOwnership, DefaultRowOwnership>();

        // Act
        services.AddSingleton<IRowOwnership, AlwaysTheSameOwner>();

        // Assert
        var actual = services.BuildServiceProvider().GetRequiredService<IRowOwnership>();
        Assert.IsType<AlwaysTheSameOwner>(actual);
    }

    [Fact]
    public void Registration_WhenAServiceRegistersNothing_ThePlatformDefaultIsUsed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.TryAddSingleton<IContext>(new Context());
        services.TryAddSingleton<IRowOwnership, DefaultRowOwnership>();

        // Act
        var actual = services.BuildServiceProvider().GetRequiredService<IRowOwnership>();

        // Assert
        Assert.IsType<DefaultRowOwnership>(actual);
    }

    private sealed class AlwaysTheSameOwner : IRowOwnership
    {
        public Guid RequireOrganization() => Guid.Empty;

        public Guid RequireUser() => Guid.Empty;

        public Guid RequireProvider() => Guid.Empty;

        public Guid? Organization() => Guid.Empty;

        public Guid? User() => Guid.Empty;

        public Guid? Provider() => Guid.Empty;
    }
}
