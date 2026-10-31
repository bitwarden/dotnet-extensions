using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// The version check every update makes: a version sent must be the one stored, and no version
/// sent means the caller did not ask for the check.
/// </summary>
public sealed class ConcurrencyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    public void Require_NoVersionOrTheStoredOne_Passes(string? sent)
    {
        Concurrency.Require(sent, 3);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("03")]
    [InlineData("")]
    public void Require_AnotherVersion_Conflicts(string sent)
    {
        Assert.Throws<ConflictException>(() => Concurrency.Require(sent, 3));
    }
}
