using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.MessageBroker.Microbenchmarks;

public class RabbitBenchmarks : MessageBrokerBenchmarks
{
    private IContainer? _container;

    protected override async Task StartInfrastructureAsync()
    {
        if (Environment.GetEnvironmentVariable("RABBITMQ_URI") is not null)
            return;

        _container = new ContainerBuilder()
            .WithImage("rabbitmq")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672))
            .Build();
        await _container.StartAsync();
    }

    protected override async Task StopInfrastructureAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    protected override Dictionary<string, string?> CreateConfig() =>
        new()
        {
            {
                "RabbitUri",
                Environment.GetEnvironmentVariable("RABBITMQ_URI")
                    ?? $"amqp://guest:guest@{_container!.Hostname}:{_container.GetMappedPublicPort(5672)}/"
            }
        };
}
