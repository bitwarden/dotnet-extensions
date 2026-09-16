using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Shared xUnit class-fixture that starts a RabbitMQ container used by every RabbitMQ-backed
/// test class in this project.
/// </summary>
public class RabbitFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string GetUri() =>
        $"amqp://guest:guest@{_container!.Hostname}:{_container.GetMappedPublicPort(5672)}/";

    public async ValueTask InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("rabbitmq")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
            .Build();

        await _container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }
}
