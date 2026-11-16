using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// A command a queue's receiver can't decode is set aside, so it can't block the families behind it
/// on the same queue, and the consumer never sees it.
/// </summary>
public sealed class CommandFamilyHandlerTests : IDisposable
{
    private const string Queue = "mail.commands";

    private readonly ServiceProvider _services;
    private readonly CommandRecorder _recorder;
    private readonly CommandFamilyHandler<SendMailPayload, SendMail, SendMailConsumer> _handler;

    public CommandFamilyHandlerTests()
    {
        _services = new ServiceCollection()
            .AddMetrics()
            .AddOptions()
            .AddSingleton<CommandRecorder>()
            .AddScoped<SendMailConsumer>()
            .BuildServiceProvider();
        _recorder = _services.GetRequiredService<CommandRecorder>();
        _handler = new CommandFamilyHandler<SendMailPayload, SendMail, SendMailConsumer>(
            Queue,
            new SystemTextJsonMessageSerializer(
                _services.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), Queue),
            _services.GetRequiredService<IServiceScopeFactory>(),
            new MessageBrokerMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            Options.Create(new MessagingOptions()));
    }

    public void Dispose() => _services.Dispose();

    private sealed class Settlement
    {
        public bool Completed { get; set; }
        public bool Requeued { get; set; }
        public string? DeadLetterReason { get; set; }
        public bool DeadLettered { get; set; }
    }

    private static CommandQueueMessage Message(string body, Settlement settlement) => new()
    {
        Body = Encoding.UTF8.GetBytes(body),
        FamilyWireName = SendMailPayload.WireName,
        MessageId = Guid.NewGuid().ToString(),
        TraceParent = null,
        DeliveryCount = 1,
        CompleteAsync = _ => { settlement.Completed = true; return Task.CompletedTask; },
        RequeueAsync = _ => { settlement.Requeued = true; return Task.CompletedTask; },
        DeadLetterAsync = (reason, _) => { settlement.DeadLettered = true; settlement.DeadLetterReason = reason; return Task.CompletedTask; },
    };

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"$type":"v1","Id":1}""")]
    public async Task ABodyThatCannotBeDeserialized_IsDeadLettered(string body)
    {
        var settlement = new Settlement();

        await _handler.HandleAsync(Message(body, settlement), TestContext.Current.CancellationToken);

        Assert.True(settlement.DeadLettered);
        Assert.False(settlement.Completed || settlement.Requeued);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task ACommandWithNoVersionThisReceiverKnows_IsDeadLettered()
    {
        var settlement = new Settlement();

        await _handler.HandleAsync(Message("""[{"$type":"v9","Id":1}]""", settlement), TestContext.Current.CancellationToken);

        Assert.True(settlement.DeadLettered);
        Assert.Contains("No version", settlement.DeadLetterReason);
        Assert.Empty(_recorder.Handled);
    }

    [Fact]
    public async Task ACommandAfterOneSetAside_IsStillHandled()
    {
        await _handler.HandleAsync(Message("not json at all", new Settlement()), TestContext.Current.CancellationToken);
        var settlement = new Settlement();

        await _handler.HandleAsync(Message("""[{"$type":"v1","Id":7}]""", settlement), TestContext.Current.CancellationToken);

        Assert.True(settlement.Completed);
        Assert.Equal(7, Assert.Single(_recorder.Handled).Id);
    }
}
