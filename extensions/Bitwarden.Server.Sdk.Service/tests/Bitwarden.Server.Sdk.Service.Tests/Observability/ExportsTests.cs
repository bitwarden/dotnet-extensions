using Bitwarden.Server.Sdk.Service.Observability;

namespace Bitwarden.Server.Sdk.Service.Tests.Observability;

/// <summary>
/// Which signals are sent is a value: nothing by default, and two choices are the same when every switch is.
/// </summary>
public sealed class ExportsTests
{
    [Fact]
    public void Default_SendsNothing() =>
        Assert.Equal(new Exports(Traces: false, Metrics: false, Logs: false), default);

    [Fact]
    public void Equality_IsBySwitch()
    {
        Assert.Equal(new Exports(true, false, true), new Exports(true, false, true));
        Assert.NotEqual(new Exports(true, false, true), new Exports(true, true, true));
    }
}
