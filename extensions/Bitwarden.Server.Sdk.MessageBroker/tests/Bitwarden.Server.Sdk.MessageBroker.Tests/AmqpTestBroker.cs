using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// The AMQP broker the RabbitMQ tests run against.
/// </summary>
internal static class AmqpTestBroker
{
    /// <summary>
    /// RabbitMQ 4.3.6, the version the self-hosted install ships, pinned by digest so every run pulls
    /// the same broker, unless <c>MESSAGING_TESTS_AMQP_IMAGE</c> names another AMQP 0-9-1 broker's image,
    /// such as <c>cloudamqp/lavinmq</c>, which Lite ships.
    /// </summary>
    public static string Image =>
        System.Environment.GetEnvironmentVariable("MESSAGING_TESTS_AMQP_IMAGE") is { Length: > 0 } image
            ? image
            : "rabbitmq@sha256:3e5cc23235b0eb850f604e8ace10d9a9b6d05be3e36730e53522ee9ead707a9e"; // 4.3.6

    /// <summary>
    /// Ready once RabbitMQ or LavinMQ logs the end of its startup.
    /// </summary>
    public static IWaitForContainerOS Ready =>
        Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete|Finished startup");
}
