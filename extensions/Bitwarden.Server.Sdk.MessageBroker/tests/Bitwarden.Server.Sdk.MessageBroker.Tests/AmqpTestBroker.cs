using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// The AMQP broker the RabbitMQ tests run against.
/// </summary>
internal static class AmqpTestBroker
{
    /// <summary>
    /// RabbitMQ 4.3.6, the version the self-hosted install ships.
    /// </summary>
    public static string Image => "rabbitmq:4.3.6";

    /// <summary>
    /// Ready once RabbitMQ logs the end of its startup.
    /// </summary>
    public static IWaitForContainerOS Ready =>
        Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete");
}
