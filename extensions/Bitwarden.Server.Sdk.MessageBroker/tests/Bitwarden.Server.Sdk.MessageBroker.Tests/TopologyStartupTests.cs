using Azure.Messaging.ServiceBus.Administration;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Environment.Setup;
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
        host.Services.GetServices<IHostedService>().OfType<TopologyStartupService>().Single().SetExitCode = c => code = c;
        exitCode = () => code;
        return host;
    }

    internal static async Task WaitForLogAsync(IHost host, LogLevel level, string text)
    {
        var logs = host.Services.GetFakeLogCollector();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (!logs.GetSnapshot().Any(r => r.Level == level && r.Message.Contains(text)))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Nothing at {level} containing \"{text}\" was logged. It logged:\n" + string.Join('\n',
                logs.GetSnapshot().Select(r => $"{r.Level}: {r.Message} {r.Exception?.GetType().Name} {r.Exception?.Message}")));
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

    // The emulator reports a missing entity as an outage, so only existing topology can be tested here.
    [Fact(Timeout = 60 * 1000)]
    public async Task ServiceWhoseSubscriptionsAndQueueExistKeepsRunning()
    {
        using var host = TopologyCheckTests.Build(
            new() { ["AzureServiceBusConnectionString"] = _fixture.GetConnectionString() },
            services =>
            {
                // Requirement only for the queue: a consumer would take the other tests' commands.
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

public class ProvisioningDecisionTests
{
    private sealed class FakeEnvironment(bool selfHosted) : IBitwardenEnvironment
    {
        public string Version => "0.0.0";
        public string? GitHash => null;
        public bool SelfHosted => selfHosted;
        public string? SelfHostFlavor => selfHosted ? "lite" : null;
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void ASelfHostedServiceProvisionsUnlessTheSettingSaysOtherwise(bool? setting, bool selfHosted, bool provisions)
    {
        var options = new MessagingOptions { RabbitUri = "amqp://localhost/", AutoProvisionTopology = setting };

        Assert.Equal(provisions, TopologyStartupService.ShouldProvision(options, () => new FakeEnvironment(selfHosted)));
    }

    [Fact]
    public void TheInMemoryChannelIsNeverProvisioned()
    {
        Assert.False(TopologyStartupService.ShouldProvision(new MessagingOptions { AutoProvisionTopology = true }, () => new FakeEnvironment(true)));
    }

    [Fact]
    public void ASelfHostedHostProvisionsByDefault()
    {
        using var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["RabbitUri"] = "amqp://guest:guest@localhost:1/" }))
            .ConfigureServices(services =>
            {
                services.AddPublisher<UserCreatedPayload, UserCreated>("user.events");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
                services.Configure<SelfHostDetails>(details => details.MakeSelfHost("lite"));
            })
            .Build();

        Assert.True(host.Services.GetRequiredService<TopologyGate>().Provisions);
    }
}

public class RabbitProvisioningTests : IClassFixture<RabbitBehaviorTests.RabbitFixture>
{
    private readonly RabbitBehaviorTests.RabbitFixture _fixture;
    private readonly string _prefix = Guid.NewGuid().ToString("N")[..8];

    public RabbitProvisioningTests(RabbitBehaviorTests.RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    private string Topic => $"{_prefix}.events";
    private string Queue => $"{_prefix}.commands";

    private void Register(IServiceCollection services)
    {
        services.AddSingleton<RetryDelayTests.DeliveryClock>();
        services.AddPublisher<MyItemPayload, MyItem>(Topic);
        // Returned for redelivery until the queue's delivery limit dead-letters it.
        services.AddMessageConsumer<MyItemPayload, MyItem, RetryDelayTests.AlwaysFailsItemConsumer>(Topic, "vault", retry => retry.MaxDeliveries = 5);
        services.AddCommandSender<SendMailPayload, SendMail>(Queue);
        services.AddCommandConsumer<SendMailPayload, SendMail, RetryDelayTests.AlwaysFailsMailConsumer>(Queue);
    }

    private Dictionary<string, string?> Config(string? uri = null) => new()
    {
        ["RabbitUri"] = uri ?? _fixture.GetUri(),
        ["AutoProvisionTopology"] = "true",
        ["MaxDeliveryCount"] = "2",
    };

    [Fact(Timeout = 90 * 1000)]
    public async Task AServiceCreatesItsTopologyAndUsesIt()
    {
        var token = TestContext.Current.CancellationToken;
        using var host = TopologyCheckTests.Build(Config(), Register, out var exitCode);
        await host.StartAsync(token);
        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information, "Created topology this service needs");

        await host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>(Topic))
            .Publish(new MyItem(1)).SendAsync(token);
        await host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(MessageBrokerKeys.Publisher<SendMailPayload>(Queue))
            .Publish(new SendMail(1)).SendAsync(token);

        // The event is delivered twice, MaxDeliveryCount, before the queue's limit dead-letters it; the
        // command once, since its consumer dead-letters on the first failure.
        Assert.NotNull(await GetDeadLetteredAsync($"dead-letter.{Topic}.vault"));
        Assert.NotNull(await GetDeadLetteredAsync($"dead-letter.{Queue}"));
        await host.StopAsync(token);

        Assert.Equal(3, host.Services.GetRequiredService<RetryDelayTests.DeliveryClock>().Count);
        Assert.Null(exitCode());
        var created = Assert.Single(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Message.StartsWith("Created topology")).Message;
        Assert.Contains($"topic exchange '{Topic}'", created);
        Assert.Contains($"subscription queue '{Topic}.vault'", created);
        Assert.Contains($"command queue '{Queue}'", created);
        Assert.Contains($"command queue exchange '{Queue}'", created);
    }

    [Fact(Timeout = 90 * 1000)]
    public async Task ASecondStartFindsItAllAndLeavesWhatAnOperatorMadeAlone()
    {
        var token = TestContext.Current.CancellationToken;
        // Made as definitions.json makes it, without arguments, so provisioning must leave it alone.
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using (var connection = await factory.CreateConnectionAsync(token))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: token))
        {
            await channel.QueueDeclareAsync($"{Topic}.vault", durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: token);
        }

        using (var first = TopologyCheckTests.Build(Config(), Register, out var firstExit))
        {
            await first.StartAsync(token);
            await TopologyCheckTests.WaitForLogAsync(first, LogLevel.Information, "Created topology this service needs");
            await first.StopAsync(token);
            Assert.Null(firstExit());
            Assert.DoesNotContain($"subscription queue '{Topic}.vault'",
                first.Services.GetFakeLogCollector().GetSnapshot().Single(r => r.Message.StartsWith("Created topology")).Message);
        }

        using var second = TopologyCheckTests.Build(Config(), Register, out var secondExit);
        await second.StartAsync(token);
        await TopologyCheckTests.WaitForLogAsync(second, LogLevel.Information, "already has the topology this service needs");
        await second.StopAsync(token);
        Assert.Null(secondExit());
    }

    [Fact(Timeout = 90 * 1000)]
    public async Task AServiceThatMayNotCreateItsTopologyStopsAndNamesIt()
    {
        var token = TestContext.Current.CancellationToken;
        var user = $"p{_prefix}";
        await _fixture.AddUserAsync(user, "password", "^$", ".*", ".*");

        using var host = TopologyCheckTests.Build(Config(_fixture.GetUri(user, "password")),
            services => services.AddPublisher<MyItemPayload, MyItem>(Topic), out var exitCode);
        await host.StartAsync(token);
        await TopologyCheckTests.WaitForStopAsync(host);
        await host.StopAsync(token);

        Assert.Equal(1, exitCode());
        var critical = Assert.Single(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level == LogLevel.Critical);
        Assert.Contains($"topic exchange '{Topic}' couldn't be created", critical.Message);
    }

    [Fact(Timeout = 90 * 1000)]
    public async Task ProvisionedTopologyWithTheNarrowestRightsIsLeftAsItIs()
    {
        var token = TestContext.Current.CancellationToken;
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using (var connection = await factory.CreateConnectionAsync(token))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: token))
        {
            var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
            await channel.ExchangeDeclareAsync(Topic, ExchangeType.Topic, durable: true, cancellationToken: token);
            await channel.QueueDeclareAsync($"{Topic}.vault", durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: token);
            await channel.QueueBindAsync($"{Topic}.vault", Topic, MyItemPayload.WireName, cancellationToken: token);
            await channel.ExchangeDeclareAsync(Queue, ExchangeType.Direct, durable: true, cancellationToken: token);
            await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: token);
            await channel.QueueBindAsync(Queue, Queue, Queue, cancellationToken: token);
        }
        var user = $"n{_prefix}";
        var topic = Topic.Replace(".", "\\.");
        var queue = Queue.Replace(".", "\\.");
        await _fixture.AddUserAsync(user, "password", "^$", $"^({topic}|{queue})$", $"^({topic}\\.vault|{queue})$");

        using var host = TopologyCheckTests.Build(Config(_fixture.GetUri(user, "password")), Register, out var exitCode);
        await host.StartAsync(token);
        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information, "already has the topology this service needs");
        await host.StopAsync(token);

        Assert.Null(exitCode());
        Assert.DoesNotContain(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level == LogLevel.Critical);
    }

    // Exactly the rights BROKER-TOPOLOGY.md lists for a service that creates its own topology.
    [Fact(Timeout = 90 * 1000)]
    public async Task TheDocumentedRightsAreEnoughToProvision()
    {
        var token = TestContext.Current.CancellationToken;
        var user = $"d{_prefix}";
        var topic = Topic.Replace(".", "\\.");
        var queue = Queue.Replace(".", "\\.");
        var queues = $"{topic}\\.vault|dead-letter\\.{topic}\\.vault|{queue}|dead-letter\\.{queue}";
        // LavinMQ refuses a queue with a dead-letter exchange unless the user may read every name (see
        // BROKER-TOPOLOGY.md, "Where RabbitMQ and LavinMQ differ").
        var read = AmqpTestBroker.Image.Contains("lavinmq", StringComparison.OrdinalIgnoreCase) ? ".*" : $"^({topic}|{queues})$";
        await _fixture.AddUserAsync(user, "password", $"^({topic}|{queues})$", $"^({topic}|{queues})$", read);

        using var host = TopologyCheckTests.Build(Config(_fixture.GetUri(user, "password")), Register, out var exitCode);
        await host.StartAsync(token);
        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information, "Created topology this service needs");
        await host.StopAsync(token);

        Assert.Null(exitCode());
        Assert.DoesNotContain(host.Services.GetFakeLogCollector().GetSnapshot(), r => r.Level >= LogLevel.Warning && r.Message.Contains("Provisioning left"));
    }

    // A topic it subscribes to is its owner's to create: the subscriber waits for it, then finishes.
    [Fact(Timeout = 90 * 1000)]
    public async Task ASubscriberWaitsForItsTopicsOwner()
    {
        var token = TestContext.Current.CancellationToken;
        var foreign = $"{_prefix}.other.events";
        using var host = TopologyCheckTests.Build(Config(),
            services => services.AddSubscriber<MyItemPayload, MyItem>(foreign, "vault"), out var exitCode);
        await host.StartAsync(token);
        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Warning, $"topic exchange '{foreign}', which its owner creates");

        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using (var connection = await factory.CreateConnectionAsync(token))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: token))
        {
            await channel.ExchangeDeclareAsync(foreign, ExchangeType.Topic, durable: true, cancellationToken: token);
        }
        await TopologyCheckTests.WaitForLogAsync(host, LogLevel.Information, "already has the topology this service needs");
        await host.StopAsync(token);

        Assert.Null(exitCode());
    }

    private async Task<BasicGetResult?> GetDeadLetteredAsync(string queue)
    {
        var token = TestContext.Current.CancellationToken;
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using var connection = await factory.CreateConnectionAsync(token);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: token);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await channel.BasicGetAsync(queue, autoAck: true, token) is { } result)
                return result;
            await Task.Delay(100, token);
        }
        return null;
    }
}

// The Azure Service Bus emulator has no management endpoint (see AzureServiceBusFixture), so provisioning
// can't run against it; these check the settings it creates entities with, which match the cloud's template.
public class AzureServiceBusProvisioningTests
{
    [Fact]
    public void TopicsDetectDuplicatesForTenMinutes()
    {
        var options = TopologyStartupService.TopicOptions("user.events");

        Assert.Equal("user.events", options.Name);
        Assert.True(options.RequiresDuplicateDetection);
        Assert.Equal(TimeSpan.FromMinutes(10), options.DuplicateDetectionHistoryTimeWindow);
    }

    [Fact]
    public void SubscriptionsAndQueuesTakeTheDeliveryLimitAndDeadLetter()
    {
        var subscription = TopologyStartupService.SubscriptionOptions("user.events", "vault", 7);
        var queue = TopologyStartupService.QueueOptions("vault.commands", 7);

        Assert.Equal(("user.events", "vault"), (subscription.TopicName, subscription.SubscriptionName));
        Assert.Equal(7, subscription.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromMinutes(1), subscription.LockDuration);
        Assert.True(subscription.DeadLetteringOnMessageExpiration);
        Assert.True(subscription.EnableDeadLetteringOnFilterEvaluationExceptions);
        Assert.Equal("vault.commands", queue.Name);
        Assert.Equal(7, queue.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromMinutes(1), queue.LockDuration);
        Assert.True(queue.DeadLetteringOnMessageExpiration);
        Assert.True(queue.RequiresDuplicateDetection);
        Assert.Equal(TimeSpan.FromMinutes(10), queue.DuplicateDetectionHistoryTimeWindow);
    }

    [Fact]
    public void AFamilyNoRuleAdmitsIsMissing()
    {
        RuleFilter[] filters = [TopologyStartupService.FamilyFilter("user.created"), new SqlRuleFilter("1 = 0")];

        Assert.Equal(["user.deleted"], TopologyStartupService.MissingFamilies(filters, ["user.created", "user.deleted"]));
    }

    [Fact]
    public void AnUnchangedDefaultRuleAdmitsEveryFamily()
    {
        Assert.Empty(TopologyStartupService.MissingFamilies([new TrueRuleFilter()], ["user.created", "user.deleted"]));
    }

    [Fact]
    public void ACorrelationFilterOnMoreThanTheFamilyDoesNotCount()
    {
        var filter = TopologyStartupService.FamilyFilter("user.created");
        filter.Subject = "something";
        filter.ApplicationProperties["other"] = "value";

        Assert.Equal(["user.created"], TopologyStartupService.MissingFamilies([filter], ["user.created"]));
    }

    [Fact]
    public void ASubscriptionAdmitsItsFamilyAlone()
    {
        var filter = TopologyStartupService.FamilyFilter("user.deleted");

        Assert.Equal("user.deleted", filter.ApplicationProperties["x-bw-payload-family"]);
    }
}
