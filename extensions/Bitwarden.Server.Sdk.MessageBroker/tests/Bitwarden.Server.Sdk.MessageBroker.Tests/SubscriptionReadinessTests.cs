using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class SubscriptionReadinessTests
{
    [Fact(Timeout = 60 * 1000)]
    public async Task ConsumersReportReadyOnceTheyAreReceiving()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddHealthChecks();
                services.AddSingleton<ItemRecorder>();
                services.AddSingleton<CommandRecorder>();
                services.AddPublisher<MyItemPayload, MyItem>("ready");
                services.AddMessageConsumer<MyItemPayload, MyItem, RecordingItemConsumer>("ready", "ready");
                services.AddCommandSender<SendMailPayload, SendMail>("ready.commands");
                services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("ready.commands");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var health = host.Services.GetRequiredService<HealthCheckService>();

        HealthReport report;
        do
        {
            report = await health.CheckHealthAsync(TestContext.Current.CancellationToken);
            if (report.Status != HealthStatus.Healthy)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        } while (report.Status != HealthStatus.Healthy);

        await host.StopAsync(TestContext.Current.CancellationToken);
        Assert.Contains(SubscriptionHealthCheckRegistration.Name, report.Entries.Keys);
    }

    [Fact]
    public void TheCheckIsDegradedAndTaggedMonitoringNeverUnhealthy()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks();
        services.AddMessageConsumer<MyItemPayload, MyItem, RecordingItemConsumer>("ready", "ready");
        using var provider = services.BuildServiceProvider();

        var registration = Assert.Single(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            each => each.Name == SubscriptionHealthCheckRegistration.Name);

        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
        Assert.Equal(["monitoring"], registration.Tags);
    }

    [Fact]
    public void ACheckNamesEveryConsumerNotYetReceiving()
    {
        var readiness = new SubscriptionReadiness();
        readiness.Expect("orders/billing");
        readiness.Expect("notification.commands");
        readiness.Receiving("orders/billing");
        readiness.Failing("notification.commands");

        var result = readiness.Check();

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("notification.commands", result.Description);
        Assert.DoesNotContain("orders/billing", result.Description);
    }
}
