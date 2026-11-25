using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

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
            new MessageBrokerMetrics(_services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()));
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
    public async Task ACommandWhoseVersionCannotBeUpcastToTheCeiling_IsDeadLettered()
    {
        using var services = new ServiceCollection()
            .AddMetrics()
            .AddOptions()
            .AddSingleton<CommandRecorder>()
            .AddScoped<RenameConsumer>()
            .BuildServiceProvider();
        var handler = new CommandFamilyHandler<RenamePayload, RenameV2, RenameConsumer>(
            Queue,
            new SystemTextJsonMessageSerializer(
                services.GetRequiredService<IOptionsMonitor<MessageBrokerSerializerOptions>>(), Queue),
            services.GetRequiredService<IServiceScopeFactory>(),
            new MessageBrokerMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()));
        var settlement = new Settlement();

        // v1 with no name: RenameV1.Upcast() throws on it, so no version reaches the RenameV2 ceiling.
        await handler.HandleAsync(Message("""[{"$type":"v1","Name":null}]""", settlement), TestContext.Current.CancellationToken);

        Assert.True(settlement.DeadLettered);
        Assert.False(settlement.Completed || settlement.Requeued);
        Assert.Contains(nameof(RenameV2), settlement.DeadLetterReason);
        Assert.Empty(services.GetRequiredService<CommandRecorder>().Handled);
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

public sealed record RenameV1(string? Name) : RenamePayload.IFloor<RenameV2>
{
    public RenameV2 Upcast() => new(Name!.Trim());
}

public sealed record RenameV2(string Name) : RenamePayload.IPureCeiling<RenameV1>
{
    public RenameV1 Downcast() => new(Name);
}

public class RenamePayload : Payload<RenamePayload, RenameV1, RenameV2>, IPayloadVariants<RenamePayload>, IPayloadWireName<RenamePayload>
{
    public static string WireName => "rename";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(RenameV1), "v1"), (typeof(RenameV2), "v2")];
}

public sealed class RenameConsumer(CommandRecorder recorder) : IMessageConsumer<RenamePayload, RenameV2>
{
    public Task HandleAsync(Envelope<RenamePayload, RenameV2> envelope, CancellationToken cancellationToken)
    {
        recorder.Record(new(RenamePayload.WireName, 0, envelope.MessageId, envelope.DeliveryCount));
        return Task.CompletedTask;
    }
}
