using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class ExpectedTopologyTests
{
    [Fact]
    public void ItIsReadFromTheRegistrationsRequirements()
    {
        var services = new ServiceCollection();
        services.AddPublisher<UserCreatedPayload, UserCreated>("user.events");
        services.AddPublisher<UserDeletedPayload, UserDeleted>("user.events");
        services.AddSubscriber<UserDeletedPayload, UserDeleted>("user.events", "vault");
        services.AddCommandSender<SendMailPayload, SendMail>("notification.commands");
        services.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>("vault.commands");
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("vault.commands");
        using var provider = services.BuildServiceProvider();

        var expected = ExpectedTopology.From(provider.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements);

        Assert.Equal(["user.events"], expected.Topics);
        Assert.Equal([("user.events", "vault")], expected.Subscriptions);
        Assert.Equal(["vault.commands"], expected.CommandQueues);
        Assert.Equal(["notification.commands"], expected.SentToQueues);
    }
}

public class TopologyCheckTests
{
    [Fact(Timeout = 30 * 1000)]
    public async Task AnUnreachableBrokerDoesNotStopTheService()
    {
        using var host = Build(new() { ["RabbitUri"] = "amqp://guest:guest@localhost:1/" },
            services => services.AddPublisher<UserCreatedPayload, UserCreated>("user.events"), out var exitCode);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await WaitForLogAsync(host, LogLevel.Warning, "Couldn't check the message broker's topology yet");
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(exitCode());
        Assert.DoesNotContain(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level == LogLevel.Critical);
    }

    internal static IHost Build(Dictionary<string, string?> config, Action<IServiceCollection> configure, out Func<int?> exitCode)
    {
        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(config))
            .ConfigureServices(services =>
            {
                services.AddFakeLogging();
                configure(services);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        int? code = null;
        host.Services.GetServices<IHostedService>().OfType<TopologyCheckService>().Single().SetExitCode = c => code = c;
        exitCode = () => code;
        return host;
    }

    internal static async Task WaitForLogAsync(IHost host, LogLevel level, string text)
    {
        var logs = host.Services.GetFakeLogCollector();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!logs.GetSnapshot().Any(r => r.Level == level && r.Message.Contains(text)))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Nothing at {level} containing \"{text}\" was logged.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    internal static async Task WaitForStopAsync(IHost host)
    {
        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!stopping.IsCancellationRequested)
        {
            Assert.True(DateTime.UtcNow < deadline, "The service didn't stop. It logged:\n" + string.Join('\n',
                host.Services.GetFakeLogCollector().GetSnapshot().Select(r => $"{r.Level}: {r.Message} {r.Exception?.GetType().Name} {r.Exception?.Message}")));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}

public class RabbitTopologyCheckTests : IClassFixture<RabbitBehaviorTests.RabbitFixture>
{
    private readonly RabbitBehaviorTests.RabbitFixture _fixture;
    private readonly string _prefix = Guid.NewGuid().ToString("N")[..8];

    public RabbitTopologyCheckTests(RabbitBehaviorTests.RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    private string Topic => $"{_prefix}.events";
    private string OwnQueue => $"{_prefix}.commands";
    private string OtherQueue => $"{_prefix}.other.commands";

    private void Register(IServiceCollection services)
    {
        services.AddPublisher<UserCreatedPayload, UserCreated>(Topic);
        services.AddSubscriber<UserDeletedPayload, UserDeleted>(Topic, "vault");
        services.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>(OwnQueue);
        services.AddCommandSender<SendMailPayload, SendMail>(OtherQueue);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ServiceWhoseTopologyExistsKeepsRunning()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using (var connection = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            var token = TestContext.Current.CancellationToken;
            await channel.ExchangeDeclareAsync(Topic, ExchangeType.Topic, durable: true, cancellationToken: token);
            await channel.QueueDeclareAsync($"{Topic}.vault", durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
            foreach (var queue in new[] { OwnQueue, OtherQueue })
            {
                await channel.ExchangeDeclareAsync(queue, ExchangeType.Direct, durable: true, cancellationToken: token);
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: token);
                await channel.QueueBindAsync(queue, queue, queue, cancellationToken: token);
            }
        }
        using var host = TopologyCheckTests.Build(new() { ["RabbitUri"] = _fixture.GetUri() }, Register, out var exitCode);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information, "has the topology this service needs");
        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested;
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(stopping);
        Assert.Null(exitCode());
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ServiceWhoseTopologyIsMissingStopsAndNamesWhatIsMissing()
    {
        using var host = TopologyCheckTests.Build(new() { ["RabbitUri"] = _fixture.GetUri() }, Register, out var exitCode);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await TopologyCheckTests.WaitForStopAsync(host);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode());
        var critical = Assert.Single(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level == LogLevel.Critical);
        Assert.Contains($"topic exchange '{Topic}' doesn't exist", critical.Message);
        Assert.Contains($"subscription queue '{Topic}.vault' doesn't exist", critical.Message);
        Assert.Contains($"command queue '{OwnQueue}' doesn't exist", critical.Message);
        Assert.Contains($"exchange of the command queue '{OtherQueue}' doesn't exist", critical.Message);
    }

    // RabbitMQ refuses a passive declare of an object the user has no rights to, whether or not it exists;
    // LavinMQ answers as it would for anyone.
    [Fact(Timeout = 60 * 1000)]
    public async Task ObjectsTheServiceHasNoRightsToAreReportedAsTheBrokerAnswers()
    {
        var token = TestContext.Current.CancellationToken;
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using (var connection = await factory.CreateConnectionAsync(token))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: token))
        {
            await channel.ExchangeDeclareAsync(Topic, ExchangeType.Topic, durable: true, cancellationToken: token);
        }
        var user = $"u{_prefix}";
        await _fixture.AddUserAsync(user, "password", "^$", "^$", "^$");
        var missingTopic = $"{_prefix}.missing.events";

        using var host = TopologyCheckTests.Build(new() { ["RabbitUri"] = _fixture.GetUri(user, "password") }, services =>
        {
            services.AddPublisher<UserCreatedPayload, UserCreated>(Topic);
            services.AddPublisher<UserDeletedPayload, UserDeleted>(missingTopic);
        }, out var exitCode);
        await host.StartAsync(token);

        await TopologyCheckTests.WaitForStopAsync(host);
        await host.StopAsync(token);

        Assert.Equal(1, exitCode());
        var critical = Assert.Single(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level == LogLevel.Critical);
        if (AmqpTestBroker.Image.Contains("lavinmq", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains($"topic exchange '{missingTopic}' doesn't exist", critical.Message);
            Assert.DoesNotContain($"'{Topic}'", critical.Message);
        }
        else
        {
            Assert.Contains($"topic exchange '{missingTopic}' is missing or the service may not use it", critical.Message);
            Assert.Contains($"topic exchange '{Topic}' is missing or the service may not use it", critical.Message);
        }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task TheCheckCanBeTurnedOff()
    {
        using var host = TopologyCheckTests.Build(
            new() { ["RabbitUri"] = _fixture.GetUri(), ["VerifyTopologyAtStartup"] = "false" },
            services => services.AddPublisher<UserCreatedPayload, UserCreated>(Topic), out var exitCode);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);
        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested;
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(stopping);
        Assert.Null(exitCode());
    }
}

[Collection(AzureServiceBusCollection.Name)]
public class AzureServiceBusTopologyCheckTests
{
    private readonly AzureServiceBusFixture _fixture;

    public AzureServiceBusTopologyCheckTests(AzureServiceBusFixture fixture)
    {
        _fixture = fixture;
    }

    // The emulator doesn't report a missing entity as Azure Service Bus does (MessagingEntityNotFound when
    // the receive link attaches): it closes the link after a timeout, which the check takes for an outage.
    // So only a service whose topology exists can be tested against it.
    [Fact(Timeout = 60 * 1000)]
    public async Task ServiceWhoseSubscriptionsAndQueueExistKeepsRunning()
    {
        using var host = TopologyCheckTests.Build(
            new() { ["AzureServiceBusConnectionString"] = _fixture.GetConnectionString() },
            services =>
            {
                // The emulator declares topic "behavior" with subscriptions "behavior" and "pm", and queue
                // "commands". The queue is declared as a requirement only: a consumer would take the other
                // tests' commands.
                services.AddSubscriber<UserCreatedPayload, UserCreated>("behavior", "pm");
                services.AddSubscriber<UserDeletedPayload, UserDeleted>("behavior", "behavior");
                MessagingRequirements.DeclareCommandConsumer(services, "commands", SendPushPayload.WireName);
            },
            out var exitCode);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information, "has the topology this service needs (3 objects checked)");
        var stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested;
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(stopping);
        Assert.Null(exitCode());
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ServiceThatOnlyPublishesSaysNothingCouldBeChecked()
    {
        using var host = TopologyCheckTests.Build(
            new() { ["AzureServiceBusConnectionString"] = _fixture.GetConnectionString() },
            services => services.AddPublisher<UserCreatedPayload, UserCreated>("behavior"),
            out var exitCode);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information,
            "None of this service's topology can be checked on this broker: topic 'behavior'");
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(exitCode());
        Assert.DoesNotContain(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Message.Contains("has the"));
    }
}
