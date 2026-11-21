using Bitwarden.Server.Sdk.Context;

namespace Bitwarden.Server.Sdk.Authorization.Tests;

/// <summary>
/// A call's context as its context token says: a call straight from the service that asserted or delegated it
/// unless the token's actor chain says otherwise.
/// </summary>
public sealed class VerifiedContextTests
{
    private static readonly Guid Organization = new("0c000000-0000-4000-8000-00000000000c");
    private static readonly Guid User = new("a1000000-0000-4000-8000-00000000000a");

    private static VerifiedContext Verified(int hops = 1) =>
        new(Organization, User, null, new ContextValue(), "a-context-token", ContextToken.Delegated, hops);

    [Fact]
    public void Hops_DefaultsToOne()
    {
        var verified = new VerifiedContext(
            Organization, User, null, new ContextValue(), "a-context-token", ContextToken.Asserted);

        Assert.Equal(1, verified.Hops);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Assert.Equal(Verified(), Verified());
        Assert.NotEqual(Verified(), Verified(hops: 2));
        Assert.NotEqual(Verified(), Verified() with { Mode = ContextToken.Asserted });
    }
}
