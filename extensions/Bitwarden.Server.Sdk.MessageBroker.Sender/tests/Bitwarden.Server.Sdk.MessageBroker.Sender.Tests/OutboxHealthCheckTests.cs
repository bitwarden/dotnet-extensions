using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class OutboxHealthCheckTests : IAsyncLifetime
{
    private readonly OutboxTestHost _outbox = new();
    private IHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await BuildHealthHostAsync(_outbox);

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    /// <summary>
    /// An instance whose backlog is counted afresh every time, with health checks.
    /// </summary>
    internal static Task<IHost> BuildHealthHostAsync(OutboxTestHost outbox) => outbox.BuildAsync(services =>
    {
        services.Configure<MessageOutboxOptions>(o => o.BacklogSampleInterval = TimeSpan.Zero);
        services.AddHealthChecks();
    });

    internal static HealthCheckRegistration Registration(IHost host) =>
        host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(r => r.Name == "Message outbox (ShopContext)");

    private Task<HealthCheckResult> CheckAsync()
    {
        var registration = Registration(_host);
        return registration.Factory(_host.Services)
            .CheckHealthAsync(new HealthCheckContext { Registration = registration }, Token);
    }

    private Task InsertAsync(string messageId, DateTimeOffset dueAt, bool setAside = false) =>
        OutboxTestHost.InsertAsync(_host, new OutboxMessage
        {
            MessageId = messageId,
            Destination = Topic,
            MessageType = OrderPlacedPayload.WireName,
            Body = [],
            DueAt = dueAt.ToUnixTimeMilliseconds(),
            AvailableAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
            SetAsideAt = setAside ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : null,
        });

    [Fact]
    public async Task AnEmptyOutboxIsHealthy()
    {
        var result = await CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0L, result.Data["pending"]);
    }

    [Fact]
    public async Task AMessageSentForLaterIsNotLate()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await Sender<OrderPlacedPayload, OrderPlaced>(scope.ServiceProvider, Topic)
                .Publish(new OrderPlaced(1)).SendLaterAsync(DateTimeOffset.UtcNow.AddHours(2), Token);
            await scope.ServiceProvider.GetRequiredService<ShopContext>().SaveChangesAsync(Token);
        }

        var result = await CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(0L, result.Data["pending"]);
    }

    [Fact]
    public async Task AMessageLongPastDueDegradesTheOutboxAndThenFailsIt()
    {
        // Its retries have pushed AvailableAt into the future; DueAt still says how late it is.
        await InsertAsync("late", DateTimeOffset.UtcNow.AddMinutes(-15));
        Assert.Equal(HealthStatus.Degraded, (await CheckAsync()).Status);

        await InsertAsync("later", DateTimeOffset.UtcNow.AddHours(-2));
        var result = await CheckAsync();
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(2L, result.Data["pending"]);
    }

    [Fact]
    public async Task AMessageSetAsideFailsTheOutbox()
    {
        await InsertAsync("set-aside", DateTimeOffset.UtcNow, setAside: true);

        var result = await CheckAsync();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(1L, result.Data["setAside"]);
        Assert.Equal(0L, result.Data["pending"]);
    }
}
