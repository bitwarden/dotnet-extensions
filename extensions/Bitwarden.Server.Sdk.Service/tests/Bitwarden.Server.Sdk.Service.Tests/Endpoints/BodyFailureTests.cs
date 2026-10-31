using Bitwarden.Server.Sdk.Service.Endpoints;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A body failure is a value: two that say the same thing about the same place are the same failure.
/// </summary>
public sealed class BodyFailureTests
{
    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new BodyFailure(422, "$.data", "Unreadable."), new BodyFailure(422, "$.data", "Unreadable."));
        Assert.NotEqual(new BodyFailure(422, "$.data", "Unreadable."), new BodyFailure(415, "$.data", "Unreadable."));
    }
}
