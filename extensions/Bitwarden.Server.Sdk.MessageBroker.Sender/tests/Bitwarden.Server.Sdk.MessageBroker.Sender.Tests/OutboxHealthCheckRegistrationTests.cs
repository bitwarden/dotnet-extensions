using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class OutboxHealthCheckRegistrationTests : IAsyncDisposable
{
    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    [Fact]
    public async Task TheCheckIsForMonitoringNotReadiness()
    {
        var registration = OutboxHealthCheckTests.Registration(await OutboxHealthCheckTests.BuildHealthHostAsync(_outbox));

        Assert.Equal(["monitoring"], registration.Tags);
        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
    }
}
