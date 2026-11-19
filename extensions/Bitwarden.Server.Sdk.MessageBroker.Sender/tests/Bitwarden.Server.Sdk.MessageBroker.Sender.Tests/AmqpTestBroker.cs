using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

internal static class AmqpTestBroker
{
    /// <summary>
    /// RabbitMQ 4.3.6, the version the self-hosted install ships, unless <c>MESSAGING_TESTS_AMQP_IMAGE</c>
    /// names another AMQP 0-9-1 broker's image,
    /// such as <c>cloudamqp/lavinmq</c>, which Lite ships.
    /// </summary>
    public static string Image =>
        System.Environment.GetEnvironmentVariable("MESSAGING_TESTS_AMQP_IMAGE") is { Length: > 0 } image ? image : "rabbitmq:4.3.6";

    public static IWaitForContainerOS Ready =>
        Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete|Finished startup");
}
