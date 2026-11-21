using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// The AMQP broker the RabbitMQ tests run against.
/// </summary>
internal static class AmqpTestBroker
{
    /// <summary>
    /// RabbitMQ, unless <c>MESSAGING_TESTS_AMQP_IMAGE</c> names another AMQP 0-9-1 broker's image,
    /// such as <c>cloudamqp/lavinmq</c>, which Lite ships.
    /// </summary>
    public static string Image =>
        Environment.GetEnvironmentVariable("MESSAGING_TESTS_AMQP_IMAGE") is { Length: > 0 } image ? image : "rabbitmq";

    /// <summary>
    /// Ready once RabbitMQ or LavinMQ logs the end of its startup.
    /// </summary>
    public static IWaitForContainerOS Ready =>
        Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete|Finished startup");
}
