using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

/// <summary>
/// An AMQP 0-9-1 broker in a container, <see cref="AmqpTestBroker.Image"/>.
/// </summary>
public sealed class AmqpBrokerFixture : IAsyncLifetime
{
    private const int AmqpPort = 5672;

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage(AmqpTestBroker.Image)
        .WithPortBinding(AmqpPort, true)
        .WithWaitStrategy(AmqpTestBroker.Ready)
        .Build();

    /// <summary>
    /// The broker's address, with the default login, as <c>MessageBroker:RabbitUri</c> takes it.
    /// </summary>
    public string Uri => $"amqp://guest:guest@{_container.Hostname}:{_container.GetMappedPublicPort(AmqpPort)}/";

    public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
