using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives everything on one command queue and hands each message to the
/// <see cref="CommandFamilyHandler"/> registered for its payload family. One per queue, however
/// many command families the owner accepts: a queue, unlike a subscription, can't be filtered by
/// family, so one receiver has to see every message.
/// </summary>
internal sealed class CommandQueueService : BackgroundService
{
    private readonly string _queueName;
    private readonly ICommandQueueReceiver _receiver;
    private readonly Dictionary<string, CommandFamilyHandler> _handlers;
    private readonly ILogger<CommandQueueService> _logger;

    public CommandQueueService(
        string queueName,
        ICommandQueueReceiver receiver,
        IEnumerable<CommandFamilyHandler> handlers,
        ILogger<CommandQueueService> logger)
    {
        _queueName = queueName;
        _receiver = receiver;
        _handlers = handlers.Where(h => h.QueueName == queueName).ToDictionary(h => h.FamilyWireName);
        _logger = logger;
    }

    // As ConsumerBackgroundService does: a receive that fails is opened again rather than
    // faulting the service.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ResubscribeLoop.RunAsync(ReceiveAsync, _logger, _queueName, TimeProvider.System, stoppingToken);

    private async Task ReceiveAsync(Action onMessage, CancellationToken stoppingToken)
    {
        await foreach (var message in _receiver.ReceiveAsync(stoppingToken))
        {
            onMessage();
            if (message.FamilyWireName is null || !_handlers.TryGetValue(message.FamilyWireName, out var handler))
            {
                // A command this service doesn't accept: a sender's mistake, or a family this
                // service stopped accepting while a sender still sends it. Retrying can't help.
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
