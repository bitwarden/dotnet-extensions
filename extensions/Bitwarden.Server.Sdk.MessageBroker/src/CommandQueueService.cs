using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives everything on one command queue and hands each message to the
/// <see cref="CommandFamilyHandler"/> registered for its payload family.
/// </summary>
internal sealed class CommandQueueService : BackgroundService
{
    private readonly string _queueName;
    private readonly ICommandQueueReceiver _receiver;
    private readonly Dictionary<string, CommandFamilyHandler> _handlers;
    private readonly ILogger<CommandQueueService> _logger;
    private readonly SubscriptionReadiness _readiness;

    public CommandQueueService(
        string queueName,
        ICommandQueueReceiver receiver,
        IEnumerable<CommandFamilyHandler> handlers,
        ILogger<CommandQueueService> logger,
        SubscriptionReadiness readiness)
    {
        _readiness = readiness;
        readiness.Expect(queueName);
        _queueName = queueName;
        _receiver = receiver;
        _handlers = handlers.Where(h => h.QueueName == queueName).ToDictionary(h => h.FamilyWireName);
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ResubscribeLoop.RunAsync(ReceiveAsync, _logger, _queueName, _readiness, TimeProvider.System, stoppingToken);

    private async Task ReceiveAsync(Action onSubscribed, Action onMessage, CancellationToken stoppingToken)
    {
        await foreach (var message in _receiver.ReceiveAsync(onSubscribed, stoppingToken))
        {
            onMessage();
            if (message.FamilyWireName is null || !_handlers.TryGetValue(message.FamilyWireName, out var handler))
            {
                _logger.LogWarning(
                    "Command {MessageId} on queue {Queue} has family {Family}, which no consumer here accepts; dead-lettering it.",
                    message.MessageId, _queueName, message.FamilyWireName ?? "(none)");
                try
                {
                    await message.DeadLetterAsync(
                        $"No consumer accepts command family '{message.FamilyWireName ?? "(none)"}' on queue '{_queueName}'.",
                        CancellationToken.None);
                }
                catch (Exception)
                {
                    // An unsettled message is redelivered when its lock expires.
                }
                continue;
            }

            await handler.HandleAsync(message, stoppingToken);
        }
    }
}
