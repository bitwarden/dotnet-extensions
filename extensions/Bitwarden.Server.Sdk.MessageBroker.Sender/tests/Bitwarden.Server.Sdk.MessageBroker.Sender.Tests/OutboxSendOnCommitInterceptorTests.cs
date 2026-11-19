using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Bitwarden.Server.Sdk.MessageBroker.Sender.Tests.OutboxTestHost;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed class OutboxSendOnCommitInterceptorTests : IAsyncDisposable
{
    private static readonly Dictionary<string, string?> SendsOnCommit = new() { ["MessageOutbox:SendOnCommit"] = "true" };

    private readonly OutboxTestHost _outbox = new();

    public ValueTask DisposeAsync() => _outbox.DisposeAsync();

    private async Task<IHost> BuildWaitingHostAsync(bool sendOnCommit)
    {
        // A poll long enough that anything sent well inside it was not sent by a poll.
        var host = await _outbox.BuildAsync(
            services => services.PostConfigure<MessageOutboxOptions>(o => o.PollInterval = TimeSpan.FromMinutes(1)),
            settings: sendOnCommit ? SendsOnCommit : null);

        // Let the first poll find nothing, so the dispatcher is waiting out the interval when the test saves.
        await host.StartAsync(Token);
        await Task.Delay(300, Token);
        return host;
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task OnACommittedMessageIsSentWellWithinThePollInterval()
    {
        var host = await BuildWaitingHostAsync(sendOnCommit: true);

        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(31));

        await host.Services.GetRequiredService<Received>().WaitForAsync(31).WaitAsync(TimeSpan.FromSeconds(5), Token);
    }

    [Fact]
    public async Task OffACommittedMessageWaitsForThePoll()
    {
        var host = await BuildWaitingHostAsync(sendOnCommit: false);

        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(32));
        await Task.Delay(TimeSpan.FromSeconds(1.5), Token);

        Assert.Empty(host.Services.GetRequiredService<Received>().Messages);
        Assert.Single(await RowsAsync(host));
    }

    [Fact]
    public async Task OnASaveInsideATransactionSignalsOnlyWhenItCommits()
    {
        var host = await _outbox.BuildAsync(settings: SendsOnCommit);
        var wake = host.Services.GetRequiredService<OutboxWake<ShopContext>>();
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await scope.ServiceProvider.GetRequiredService<IOutboxSender>().SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(1), cancellationToken: Token);
        await database.SaveChangesAsync(Token);

        var beforeCommit = await SignalledAsync(wake);
        await transaction.CommitAsync(Token);

        Assert.False(beforeCommit);
        Assert.True(await SignalledAsync(wake));
    }

    [Fact]
    public async Task OnARolledBackTransactionRaisesNoSignal()
    {
        var host = await _outbox.BuildAsync(settings: SendsOnCommit);
        var wake = host.Services.GetRequiredService<OutboxWake<ShopContext>>();
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShopContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await scope.ServiceProvider.GetRequiredService<IOutboxSender>().SendAsync<OrderPlacedPayload, OrderPlaced>(new OrderPlaced(1), cancellationToken: Token);
        await database.SaveChangesAsync(Token);

        await transaction.RollbackAsync(Token);

        Assert.False(await SignalledAsync(wake));
    }

    [Fact]
    public async Task OnASaveWithoutOutboxMessagesRaisesNoSignal()
    {
        var host = await _outbox.BuildAsync(settings: SendsOnCommit);
        var wake = host.Services.GetRequiredService<OutboxWake<ShopContext>>();
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ShopContext>();

        database.Orders.Add(new Order { Id = 1 });
        await database.SaveChangesAsync(Token);

        Assert.False(await SignalledAsync(wake));
    }

    [Fact]
    public async Task OffASaveRaisesNoSignal()
    {
        var host = await _outbox.BuildAsync();
        var wake = host.Services.GetRequiredService<OutboxWake<ShopContext>>();

        await SendAsync<OrderPlacedPayload, OrderPlaced>(host, new OrderPlaced(1));

        Assert.False(await SignalledAsync(wake));
    }

    private static async Task<bool> SignalledAsync(OutboxWake<ShopContext> wake)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
        try
        {
            await wake.WaitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        {
            return false;
        }
    }
}
