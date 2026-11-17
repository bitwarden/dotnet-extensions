using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public sealed class ServiceBusProvisioningTests : IAsyncLifetime
{
    private const string SqlPassword = "Topology-Tests-1";
    private const int MaxDeliveryCount = 3;

    private readonly string _configFile = Path.Combine(Path.GetTempPath(), $"sb-topology-{Guid.NewGuid():N}.json");
    private INetwork? _network;
    private IContainer? _sql;
    private IContainer? _emulator;
    private IHost? _host;

    public async ValueTask InitializeAsync()
    {
        var config = ServiceBusTopology.EmulatorConfig(BrokerTopology.Collect(Sample.Manifests()),
            new ServiceBusOptions { MaxDeliveryCount = MaxDeliveryCount });
        await File.WriteAllTextAsync(_configFile, config.ToJsonString(), TestContext.Current.CancellationToken);

        _network = new NetworkBuilder().Build();
        await _network.CreateAsync(TestContext.Current.CancellationToken);
        _sql = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-sql-edge")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithNetwork(_network)
            .WithNetworkAliases("sql")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("SQL Server is now ready for client connections\\."))
            .Build();
        await _sql.StartAsync(TestContext.Current.CancellationToken);
        _emulator = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", "sql")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithNetwork(_network)
            .WithBindMount(_configFile, "/ServiceBus_Emulator/ConfigFiles/Config.json")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Emulator Service is Successfully Up!"))
            .Build();
        await _emulator.StartAsync(TestContext.Current.CancellationToken);

        _host = new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBusConnectionString"] =
                    $"Endpoint=sb://localhost:{_emulator.GetMappedPublicPort(5672)};SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;",
                // The services check the topology at startup rather than create it: only the configuration makes it.
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

        if (_emulator is not null) await _emulator.DisposeAsync();
        if (_sql is not null) await _sql.DisposeAsync();
        if (_network is not null) await _network.DisposeAsync();
        File.Delete(_configFile);
    }

    [Fact(Timeout = 180 * 1000)]
    public async Task AnEventReachesTheSubscriptionFilteredToItsFamilyAlone()
    {
        var created = new UserCreated(Guid.NewGuid());
        var deleted = new UserDeleted(Guid.NewGuid());
        await Publisher<UserCreatedPayload, UserCreated>(Sample.IdentityEvents).Publish(created).SendAsync(TestContext.Current.CancellationToken);
        await Publisher<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents).Publish(deleted).SendAsync(TestContext.Current.CancellationToken);

        var subscriber = _host!.Services.GetRequiredKeyedService<ISubscriber<UserDeletedPayload, UserDeleted>>($"{Sample.IdentityEvents}/vault");
        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            // The user.created event didn't pass the subscription's filter.
            Assert.Equal(deleted, envelope.Payload);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }
    }

    [Fact(Timeout = 180 * 1000)]
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

    [Fact(Timeout = 180 * 1000)]
    public async Task AMessageReturnedOnEveryDeliveryStopsAtTheLimit()
    {
        var deleted = new UserDeleted(Guid.NewGuid());
        await Publisher<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents).Publish(deleted).SendAsync(TestContext.Current.CancellationToken);

        var subscriber = _host!.Services.GetRequiredKeyedService<ISubscriber<UserDeletedPayload, UserDeleted>>($"{Sample.IdentityEvents}/vault");
        var deliveries = new List<int>();
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        quiet.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await foreach (var envelope in subscriber.SubscribeAsync(quiet.Token))
            {
                deliveries.Add(envelope.DeliveryCount);
                await envelope.RequeueAsync(cancellationToken: TestContext.Current.CancellationToken);
            }
        }
        catch (OperationCanceledException) { /* expected: nothing more to deliver, it's in the dead-letter queue */ }

        Assert.Equal([1, 2, 3], deliveries);
    }

    private Publisher<TPayload, TCeiling> Publisher<TPayload, TCeiling>(string destination)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        => _host!.Services.GetRequiredKeyedService<Publisher<TPayload, TCeiling>>(MessageBrokerKeys.Publisher<TPayload>(destination));
}
