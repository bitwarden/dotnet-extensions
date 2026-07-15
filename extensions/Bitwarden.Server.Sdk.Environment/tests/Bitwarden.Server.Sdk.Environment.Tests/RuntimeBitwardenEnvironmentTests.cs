using Bitwarden.Server.Sdk.Environment.Internals;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Environment.Tests;

public class RuntimeBitwardenEnvironmentTests
{
    // IVersionInfoAccessor is internal; use a hand-rolled stub rather than a substitute
    // to avoid needing InternalsVisibleTo("DynamicProxyGenAssembly2").
    private sealed class NullVersionInfoAccessor : IVersionInfoAccessor
    {
        public VersionInfo? Get() => null;
    }

    private static RuntimeBitwardenEnvironment Build(SelfHostDetails details)
        => new(new NullVersionInfoAccessor(), Options.Create(details));

    [Fact]
    public void SelfHosted_WhenConfiguredAsSelfHost_IsTrue()
    {
        var details = new SelfHostDetails();
        details.MakeSelfHost("docker");

        var sut = Build(details);

        Assert.True(sut.SelfHosted);
    }

    [Fact]
    public void SelfHostFlavor_WhenConfiguredAsSelfHost_MatchesFlavor()
    {
        var details = new SelfHostDetails();
        details.MakeSelfHost("docker");

        var sut = Build(details);

        Assert.Equal("docker", sut.SelfHostFlavor);
    }

    [Fact]
    public void SelfHosted_WhenConfiguredAsCloud_IsFalse()
    {
        var details = new SelfHostDetails();
        details.MakeCloud();

        var sut = Build(details);

        Assert.False(sut.SelfHosted);
    }

    [Fact]
    public void SelfHostFlavor_WhenConfiguredAsCloud_IsNull()
    {
        var details = new SelfHostDetails();
        details.MakeCloud();

        var sut = Build(details);

        Assert.Null(sut.SelfHostFlavor);
    }

    [Fact]
    public void SelfHosted_WhenDefault_IsFalse()
    {
        var sut = Build(new SelfHostDetails());

        Assert.False(sut.SelfHosted);
        Assert.Null(sut.SelfHostFlavor);
    }

    [Fact]
    public void Version_WhenVersionInfoUnavailable_IsEmptyString()
    {
        // NullVersionInfoAccessor returns null, exercising the ?? string.Empty fallback
        var sut = Build(new SelfHostDetails());

        Assert.Equal(string.Empty, sut.Version);
    }

    [Fact]
    public void GitHash_WhenVersionInfoUnavailable_IsNull()
    {
        var sut = Build(new SelfHostDetails());

        Assert.Null(sut.GitHash);
    }
}
