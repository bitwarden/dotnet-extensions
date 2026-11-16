using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Exceptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// What a service's registrations need to exist on its broker, read from the infrastructure
/// requirements they declare (<see cref="MessagingRequirements"/>).
/// </summary>
/// <param name="Topics">The topics it publishes to, which it owns.</param>
/// <param name="Subscriptions">The subscriptions it reads.</param>
/// <param name="CommandQueues">Its own command queues, which it reads.</param>
/// <param name="SentToQueues">Other services' command queues it sends to.</param>
internal sealed record ExpectedTopology(
    IReadOnlyList<string> Topics,
    IReadOnlyList<(string Topic, string Subscription)> Subscriptions,
    IReadOnlyList<string> CommandQueues,
    IReadOnlyList<string> SentToQueues)
{
    public bool IsEmpty => Topics.Count == 0 && Subscriptions.Count == 0 && CommandQueues.Count == 0 && SentToQueues.Count == 0;

    public static ExpectedTopology From(IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> requirements)
    {
        IEnumerable<JsonObject> Of(string kind) => requirements.TryGetValue(kind, out var list) ? list : [];
        static string Value(JsonObject requirement, string key) => (string)requirement[key]!;

        return new ExpectedTopology(
            [.. Of(MessagingRequirements.Topic).Select(r => Value(r, "topic")).Distinct().Order(StringComparer.Ordinal)],
            [.. Of(MessagingRequirements.Subscription).Select(r => (Value(r, "topic"), Value(r, "subscription"))).Distinct()
                .OrderBy(s => s.Item1, StringComparer.Ordinal).ThenBy(s => s.Item2, StringComparer.Ordinal)],
            [.. Of(MessagingRequirements.CommandQueue).Select(r => Value(r, "queue")).Distinct().Order(StringComparer.Ordinal)],
            [.. Of(MessagingRequirements.CommandSend).Select(r => Value(r, "queue")).Distinct().Order(StringComparer.Ordinal)]);
    }
}

/// <summary>
/// Checks, once the service first reaches its broker, that the topology its registrations need exists,
/// and stops the service, naming what is missing, if it doesn't (<see cref="MessagingOptions.VerifyTopologyAtStartup"/>).
/// </summary>
/// <remarks>
/// <para>
/// Topology is provisioned outside the services, so a missing topic, subscription or queue is a setup
/// mistake, not an outage: the service stops (exit code 1) rather than running without, say, the queue
/// its commands arrive on. A broker that can't be reached is an outage, which the service rides
/// through: the check waits for it, trying again after a delay that doubles from one second up to thirty.
/// </para>
/// <para>
/// RabbitMQ and LavinMQ: a passive declare of each exchange and queue the service uses, which needs no
/// configure right, only the read or write right the service already has on it. It doesn't see
/// bindings or policies. RabbitMQ refuses a passive declare of an object the service has no rights to,
/// whether or not it exists, so a refusal is reported as an object the service may not use.
/// Azure Service Bus: a peek at the service's own subscriptions and command queues, which needs only
/// the receive rights it has. Its topics and the queues it only sends to can't be checked without the
/// Manage right; a missing one shows as sends that fail.
/// </para>
/// </remarks>
internal sealed class TopologyCheckService(
    IOptions<MessagingOptions> options,
    IOptions<InfrastructureOptions> infrastructure,
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    ILogger<TopologyCheckService> logger) : BackgroundService
{
    /// <summary>Sets the process exit code; tests replace it so a stopped test host doesn't fail the run.</summary>
    internal Action<int> SetExitCode { get; set; } = code => Environment.ExitCode = code;

    /// <summary>Times the wait between attempts while the broker can't be reached.</summary>
    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var value = options.Value;
        if (!value.VerifyTopologyAtStartup)
            return;

        var expected = ExpectedTopology.From(infrastructure.Value.Requirements);
        Func<CancellationToken, Task<CheckResult>>? check =
            value.UsesAzureServiceBus ? token => CheckAzureServiceBusAsync(expected, token)
            : !string.IsNullOrEmpty(value.RabbitUri) ? token => CheckRabbitAsync(expected, token)
            : null;
        if (check is null || expected.IsEmpty)
            return;

        var delay = ResubscribeLoop.InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            CheckResult result;
            try
            {
                result = await check(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The broker can't be reached yet, or went away during the check: an outage, not a
                // setup mistake. Check again once it is back.
                logger.LogWarning(ex, "Couldn't check the message broker's topology yet; trying again in {Delay}.", delay);
                try
                {
                    await Task.Delay(delay, TimeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                delay = ResubscribeLoop.NextDelay(delay);
                continue;
            }

            if (result.Problems.Count == 0)
            {
                // Say only what was checked: on Azure Service Bus some of the topology can't be.
                if (result.Unchecked.Count == 0)
                    logger.LogInformation("The message broker has the topology this service needs ({Checked} objects checked).", result.Checked);
                else if (result.Checked == 0)
                    logger.LogInformation("None of this service's topology can be checked on this broker: {Unchecked}.",
                        string.Join("; ", result.Unchecked));
                else
                    logger.LogInformation("The message broker has the {Checked} objects checked; these can't be checked on this broker: {Unchecked}.",
                        result.Checked, string.Join("; ", result.Unchecked));
                return;
            }

            logger.LogCritical(
                "The message broker is missing topology this service needs, or the service may not use it: {Problems}. " +
                "Provision the broker's topology, then start the service again. Stopping.",
                string.Join("; ", result.Problems));
            SetExitCode(1);
            lifetime.StopApplication();
            return;
        }
    }

    /// <param name="Checked">How many objects were checked.</param>
    /// <param name="Unchecked">The objects this broker can't check.</param>
    /// <param name="Problems">Each checked object that is missing or may not be used.</param>
    private sealed record CheckResult(int Checked, IReadOnlyList<string> Unchecked, IReadOnlyList<string> Problems);

    private async Task<CheckResult> CheckRabbitAsync(ExpectedTopology expected, CancellationToken cancellationToken)
    {
        // Throws while the broker can't be reached, which the caller treats as an outage.
        var connection = await services.GetRequiredService<RabbitConnection>().GetConnectionAsync(cancellationToken);

        var objects = new List<(string Label, string Name, bool IsQueue)>();
        objects.AddRange(expected.Topics.Select(topic => ("topic exchange", topic, false)));
        objects.AddRange(expected.Subscriptions.Select(s => ("subscription queue", RabbitSubscriber.QueueName(s.Topic, s.Subscription), true)));
        objects.AddRange(expected.CommandQueues.Select(queue => ("command queue", queue, true)));
        objects.AddRange(expected.SentToQueues.Select(queue => ("exchange of the command queue", queue, false)));

        var problems = new List<string>();
        foreach (var (label, name, isQueue) in objects)
        {
            // A failed passive declare closes its channel, so each object gets one of its own.
            var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            try
            {
                if (isQueue)
                    await channel.QueueDeclarePassiveAsync(name, cancellationToken);
                else
                    await channel.ExchangeDeclarePassiveAsync(name, cancellationToken);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
            {
                problems.Add($"{label} '{name}' doesn't exist");
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 403)
            {
                problems.Add($"{label} '{name}' is missing or the service may not use it");
            }
            finally
            {
                try
                {
                    await channel.DisposeAsync();
                }
                catch (Exception)
                {
                    // A channel the broker closed can fail to dispose; nothing on it is worth keeping.
                }
            }
        }
        return new CheckResult(objects.Count, [], problems);
    }

    private async Task<CheckResult> CheckAzureServiceBusAsync(ExpectedTopology expected, CancellationToken cancellationToken)
    {
        var client = services.GetRequiredService<AzureServiceBusConnection>().Client;

        var entities = new List<(string Label, Func<ServiceBusReceiver> Open)>();
        entities.AddRange(expected.Subscriptions.Select(s =>
            ($"subscription '{s.Subscription}' on topic '{s.Topic}'", (Func<ServiceBusReceiver>)(() => client.CreateReceiver(s.Topic, s.Subscription)))));
        entities.AddRange(expected.CommandQueues.Select(queue =>
            ($"command queue '{queue}'", (Func<ServiceBusReceiver>)(() => client.CreateReceiver(queue)))));

        var problems = new List<string>();
        foreach (var (label, open) in entities)
        {
            await using var receiver = open();
            try
            {
                // A peek takes no lock and changes nothing; it fails if the entity doesn't exist.
                await receiver.PeekMessageAsync(cancellationToken: cancellationToken);
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
            {
                problems.Add($"{label} doesn't exist");
            }
            catch (UnauthorizedAccessException)
            {
                problems.Add($"{label} may not be read by the service");
            }
        }
        IReadOnlyList<string> notChecked =
        [
            .. expected.Topics.Select(topic => $"topic '{topic}'"),
            .. expected.SentToQueues.Select(queue => $"command queue '{queue}' (sent to)"),
        ];
        return new CheckResult(entities.Count, notChecked, problems);
    }
}
