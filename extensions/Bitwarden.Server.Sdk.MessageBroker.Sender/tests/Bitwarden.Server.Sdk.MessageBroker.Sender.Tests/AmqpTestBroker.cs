using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

internal static class AmqpTestBroker
{
    /// <summary>
    /// RabbitMQ 4.3.6, the version the self-hosted install ships.
    /// </summary>
    public static string Image => "rabbitmq:4.3.6";

    public static IWaitForContainerOS Ready =>
        Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete");
}
