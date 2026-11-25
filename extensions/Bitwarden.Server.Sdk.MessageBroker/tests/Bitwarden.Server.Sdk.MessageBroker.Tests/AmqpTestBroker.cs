using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// The AMQP broker the RabbitMQ tests run against.
/// </summary>
internal static class AmqpTestBroker
{
    /// <summary>
    /// The RabbitMQ image, pinned by digest so every run pulls the same broker.
    /// </summary>
    public static string Image => "rabbitmq@sha256:aa82e78ca0dfa59f4eeb9e8086ac7b5c8e0b31a7bb978d948c6d32756802fb27"; // 4.4.1

    /// <summary>
    /// Ready once RabbitMQ logs the end of its startup.
    /// </summary>
    public static IWaitForContainerOS Ready =>
        Wait.ForUnixContainer().UntilMessageIsLogged("Server startup complete");
}
