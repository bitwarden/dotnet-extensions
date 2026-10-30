namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class IReadinessCheckTests
{
    [Fact]
    public void Yes_IsReadyWithNoReason()
    {
        Assert.True(ReadinessResult.Yes.Ready);
        Assert.Null(ReadinessResult.Yes.Reason);
    }

    [Fact]
    public void No_IsNotReadyAndSaysWhy()
    {
        var result = ReadinessResult.No("The cache is unreachable.");

        Assert.False(result.Ready);
        Assert.Equal("The cache is unreachable.", result.Reason);
    }

    [Fact]
    public void Check_IsWhatTheImplementationAnswers()
    {
        IReadinessCheck check = new NotReady();

        Assert.Equal(ReadinessResult.No("Waiting for the database."), check.Check());
    }

    private sealed class NotReady : IReadinessCheck
    {
        public ReadinessResult Check() => ReadinessResult.No("Waiting for the database.");
    }
}
