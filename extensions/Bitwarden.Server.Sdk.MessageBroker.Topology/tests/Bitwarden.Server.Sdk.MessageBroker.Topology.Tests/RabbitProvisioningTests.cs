using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public sealed class RabbitProvisioningTests : IAsyncLifetime
{
    private const int MaxDeliveryCount = 3;

    private IContainer? _broker;
    private IHost? _host;

    public async ValueTask InitializeAsync()
    {
        _broker = new ContainerBuilder()
            .WithImage(AmqpTestBroker.Image)
            .WithPortBinding(5672, true)
            .WithWaitStrategy(AmqpTestBroker.Ready)
            .Build();
        await _broker.StartAsync(TestContext.Current.CancellationToken);

        var definitions = RabbitDefinitions.Build(BrokerTopology.Collect(Sample.Manifests()),
            new RabbitDefinitionsOptions { MaxDeliveryCount = MaxDeliveryCount });
        await _broker.CopyAsync(Encoding.UTF8.GetBytes(definitions.ToJsonString()), "/tmp/definitions.json",
            ct: TestContext.Current.CancellationToken);
        var control = AmqpTestBroker.Image.Contains("lavinmq", StringComparison.Ordinal) ? "lavinmqctl" : "rabbitmqctl";
        var imported = await _broker.ExecAsync([control, "import_definitions", "/tmp/definitions.json"], TestContext.Current.CancellationToken);
        Assert.True(imported.ExitCode == 0, $"{control} import_definitions: {imported.Stdout}{imported.Stderr}");

        _host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RabbitUri"] = $"amqp://guest:guest@{_broker.Hostname}:{_broker.GetMappedPublicPort(5672)}/",
                // The services check the topology at startup rather than create it: only the definitions make it.
                ["AutoProvisionTopology"] = "false",
            }))
            .ConfigureServices(services =>
            {
                services.AddMetrics();
                Sample.Identity(services);
                Sample.Vault(services);
                Sample.Notification(services);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await _host.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(CancellationToken.None);
            _host.Dispose();
        }

        if (_broker is not null)
        {
            await _broker.DisposeAsync();
        }
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task AnEventReachesTheSubscriptionBoundToItsFamilyAlone()
    {
        var created = new UserCreated(Guid.NewGuid());
        var deleted = new UserDeleted(Guid.NewGuid());
        await Publisher<UserCreatedPayload, UserCreated>(Sample.IdentityEvents).Publish(created).SendAsync(TestContext.Current.CancellationToken);
        await Publisher<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents).Publish(deleted).SendAsync(TestContext.Current.CancellationToken);

        var subscriber = _host!.Services.GetRequiredKeyedService<ISubscriber<UserDeletedPayload, UserDeleted>>($"{Sample.IdentityEvents}/vault");
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            // The user.created event went nowhere: no subscription is bound to it.
            Assert.Equal(deleted, envelope.Payload);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task ACommandReachesItsQueuesConsumer()
    {
        var mail = new SendMail(Guid.NewGuid());
        await Publisher<SendMailPayload, SendMail>(Sample.NotificationCommands).Publish(mail).SendAsync(TestContext.Current.CancellationToken);

        var recorder = _host!.Services.GetRequiredService<MailRecorder>();
        while (!recorder.Handled.Contains(mail.MailId))
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task AMessageReturnedOnEveryDeliveryIsDeadLetteredAtTheLimit()
    {
        var deleted = new UserDeleted(Guid.NewGuid());
        await Publisher<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents).Publish(deleted).SendAsync(TestContext.Current.CancellationToken);

        var subscriber = _host!.Services.GetRequiredKeyedService<ISubscriber<UserDeletedPayload, UserDeleted>>($"{Sample.IdentityEvents}/vault");
        var deliveries = new List<int>();
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        quiet.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var envelope in subscriber.SubscribeAsync(quiet.Token))
            {
                deliveries.Add(envelope.DeliveryCount);
                await envelope.RequeueAsync(cancellationToken: TestContext.Current.CancellationToken);
            }
        }
        catch (OperationCanceledException) { /* expected: nothing more to deliver */ }
        catch (BrokerDisconnectedException) { /* expected: the channel closed when the loop ended */ }

        Assert.Equal([1, 2, 3], deliveries);
        var counted = await _broker!.ExecAsync(AmqpTestBroker.Image.Contains("lavinmq", StringComparison.Ordinal)
            ? ["lavinmqctl", "list_queues", "name", "messages"]
            : ["rabbitmqctl", "list_queues", "--quiet", "name", "messages"], TestContext.Current.CancellationToken);
        Assert.Matches(@"(?m)^dead-letter\.identity\.events\.vault\s+1\s*$", counted.Stdout);
    }

    private Publisher<TPayload, TCeiling> Publisher<TPayload, TCeiling>(string destination)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        => _host!.Services.GetRequiredKeyedService<Publisher<TPayload, TCeiling>>(MessageBrokerKeys.Publisher<TPayload>(destination));
}
