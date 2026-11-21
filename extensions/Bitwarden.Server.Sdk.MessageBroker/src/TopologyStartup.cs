using System.Text.Json.Nodes;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// What a service's registrations need to exist on its broker, read from the infrastructure
/// requirements they declare (<see cref="MessagingRequirements"/>).
/// </summary>
/// <param name="Topics">The topics it publishes to, which it owns.</param>
/// <param name="Subscriptions">The subscriptions it reads.</param>
/// <param name="SubscriptionFamilies">Each family a subscription takes, which its filter or bindings admit.</param>
/// <param name="CommandQueues">Its own command queues, which it reads.</param>
/// <param name="SentToQueues">Other services' command queues it sends to.</param>
internal sealed record ExpectedTopology(
    IReadOnlyList<string> Topics,
    IReadOnlyList<(string Topic, string Subscription)> Subscriptions,
    IReadOnlyList<(string Topic, string Subscription, string Family)> SubscriptionFamilies,
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
            [.. Of(MessagingRequirements.Subscription).Select(r => (Value(r, "topic"), Value(r, "subscription"), Value(r, "family"))).Distinct()
                .OrderBy(s => s.Item1, StringComparer.Ordinal).ThenBy(s => s.Item2, StringComparer.Ordinal).ThenBy(s => s.Item3, StringComparer.Ordinal)],
            [.. Of(MessagingRequirements.CommandQueue).Select(r => Value(r, "queue")).Distinct().Order(StringComparer.Ordinal)],
            [.. Of(MessagingRequirements.CommandSend).Select(r => Value(r, "queue")).Distinct().Order(StringComparer.Ordinal)]);
    }
}

/// <summary>
/// Holds hosted consumers back until the service has provisioned its topology. Open from the start when
/// the service doesn't provision.
/// </summary>
internal sealed class TopologyGate
{
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="options">Which broker, and whether the service provisions.</param>
    /// <param name="services">
    /// Gives <see cref="IBitwardenEnvironment"/>, read only when <see cref="MessagingOptions.AutoProvisionTopology"/>
    /// is unset.
    /// </param>
    public TopologyGate(IOptions<MessagingOptions> options, IServiceProvider services)
    {
        Provisions = TopologyStartupService.ShouldProvision(options.Value, () => services.GetRequiredService<IBitwardenEnvironment>());
        if (!Provisions)
            _open.TrySetResult();
    }

    /// <summary>
    /// Whether this service creates its topology as it starts.
    /// </summary>
    public bool Provisions { get; }

    public void Open() => _open.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken) => _open.Task.WaitAsync(cancellationToken);
}

/// <summary>
/// Once the service first reaches its broker, either creates the topology its registrations need
/// (<see cref="MessagingOptions.AutoProvisionTopology"/>) or checks that it exists
/// (<see cref="MessagingOptions.VerifyTopologyAtStartup"/>), and stops the service, naming each object,
/// if it can't create one or finds one missing (exit code 1). An unreachable broker is an outage: it
/// waits for it.
/// </summary>
/// <remarks>
/// <para>
/// Provisioning creates what is missing and leaves what exists alone, whoever made it. It doesn't
/// create the topics it subscribes to or the queues it sends to, which their owners create. Hosted
/// consumers wait until it finishes.
/// </para>
/// <para>
/// Checking, RabbitMQ and LavinMQ: a passive declare of each exchange and queue, which needs only the
/// read or write right the service already has; RabbitMQ refuses one on an object the service has no
/// rights to, so a refusal is reported as an object it may not use. Azure Service Bus: a peek at the
/// service's own subscriptions and command queues; its topics and the queues it only sends to can't be
/// checked without the Manage right.
/// </para>
/// </remarks>
internal sealed class TopologyStartupService(
    IOptions<MessagingOptions> options,
    IOptions<InfrastructureOptions> infrastructure,
    TopologyGate gate,
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    ILogger<TopologyStartupService> logger) : BackgroundService
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DuplicateDetectionWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Sets the process exit code; tests replace it so a stopped test host doesn't fail the run.
    /// </summary>
    internal Action<int> SetExitCode { get; set; } = code => Environment.ExitCode = code;

    internal TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Whether a service with these options creates its topology: as set, or when self-hosted. Never with
    /// the in-memory channel, which has none.
    /// </summary>
    internal static bool ShouldProvision(MessagingOptions options, Func<IBitwardenEnvironment> environment) =>
        (options.UsesAzureServiceBus || !string.IsNullOrEmpty(options.RabbitUri))
        && (options.AutoProvisionTopology ?? environment().SelfHosted);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunAsync(stoppingToken);
        }
        finally
        {
            // A host that is stopping cancels the consumers' wait instead.
            if (!lifetime.ApplicationStopping.IsCancellationRequested)
                gate.Open();
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var value = options.Value;
        var provision = gate.Provisions;
        if (!provision && !value.VerifyTopologyAtStartup)
            return;

        var expected = ExpectedTopology.From(infrastructure.Value.Requirements);
        Func<CancellationToken, Task<StepResult>>? step = (provision, value.UsesAzureServiceBus) switch
        {
            (true, true) => token => ProvisionAzureServiceBusAsync(expected, value, token),
            (true, false) => token => ProvisionRabbitAsync(expected, value, token),
            (false, true) => token => CheckAzureServiceBusAsync(expected, token),
            (false, false) when !string.IsNullOrEmpty(value.RabbitUri) => token => CheckRabbitAsync(expected, token),
            _ => null,
        };
        if (step is null || expected.IsEmpty)
            return;

        var action = provision ? "provision" : "check";
        var delay = ResubscribeLoop.InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            StepResult result;
            try
            {
                result = await step(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (TopologyNotReadyException ex)
            {
                logger.LogWarning("Couldn't {Action} the message broker's topology yet: {Reason} Trying again in {Delay}.", action, ex.Message, delay);
                if (!await DelayAsync(delay, stoppingToken))
                    return;
                delay = ResubscribeLoop.NextDelay(delay);
                continue;
            }
            catch (Exception ex)
            {
                // Unreachable, or went away part way: an outage. What was already made is left alone.
                logger.LogWarning(ex, "Couldn't {Action} the message broker's topology yet; trying again in {Delay}.", action, delay);
                if (!await DelayAsync(delay, stoppingToken))
                    return;
                delay = ResubscribeLoop.NextDelay(delay);
                continue;
            }

            if (result.Problems.Count == 0)
            {
                LogSuccess(provision, result);
                return;
            }

            if (provision)
            {
                logger.LogCritical(
                    "The service couldn't create topology it needs on the message broker: {Problems}. " +
                    "Give it the rights to create them, or provision the broker's topology and set AutoProvisionTopology to false. Stopping.",
                    string.Join("; ", result.Problems));
            }
            else
            {
                logger.LogCritical(
                    "The message broker is missing topology this service needs, or the service may not use it: {Problems}. " +
                    "Provision the broker's topology, or set AutoProvisionTopology to true, then start the service again. Stopping.",
                    string.Join("; ", result.Problems));
            }
            SetExitCode(1);
            lifetime.StopApplication();
            return;
        }
    }

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, TimeProvider, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void LogSuccess(bool provision, StepResult result)
    {
        if (provision)
        {
            if (result.Notes.Count > 0)
                logger.LogWarning("Provisioning left these as they are, since the service may not see or change them: {Notes}.",
                    string.Join("; ", result.Notes));
            if (result.Created.Count == 0)
                logger.LogInformation("The message broker already has the topology this service needs ({Checked} objects).", result.Checked);
            else
                logger.LogInformation("Created topology this service needs on the message broker: {Created} ({Checked} objects in all).",
                    string.Join("; ", result.Created), result.Checked);
            return;
        }

        // Say only what was checked: on Azure Service Bus some of the topology can't be.
        if (result.Unchecked.Count == 0)
            logger.LogInformation("The message broker has the topology this service needs ({Checked} objects checked).", result.Checked);
        else if (result.Checked == 0)
            logger.LogInformation("None of this service's topology can be checked on this broker: {Unchecked}.",
                string.Join("; ", result.Unchecked));
        else
            logger.LogInformation("The message broker has the {Checked} objects checked; these can't be checked on this broker: {Unchecked}.",
                result.Checked, string.Join("; ", result.Unchecked));
    }

    /// <param name="Checked">How many objects were checked or provisioned.</param>
    /// <param name="Unchecked">The objects this broker can't check.</param>
    /// <param name="Created">The objects provisioning created.</param>
    /// <param name="Problems">Each object that is missing, may not be used, or couldn't be created.</param>
    private sealed record StepResult(int Checked, IReadOnlyList<string> Unchecked, IReadOnlyList<string> Created, IReadOnlyList<string> Problems)
    {
        /// <summary>
        /// What provisioning couldn't see or change on objects that already exist, left to whoever made them.
        /// </summary>
        public IReadOnlyList<string> Notes { get; init; } = [];
    }

    /// <summary>
    /// Another service hasn't created its part yet, such as the topic a subscription is on. Retried as an
    /// outage is.
    /// </summary>
    private sealed class TopologyNotReadyException(string message) : Exception(message);

    /// <summary>
    /// Runs one operation on a channel of its own, since a refused one closes its channel. Returns the
    /// broker's reply code when it refuses (403 not permitted, 404 not found, 406 exists with other
    /// settings), or null when it succeeds.
    /// </summary>
    private static async Task<ushort?> TryAsync(IConnection connection, Func<IChannel, Task> operation, CancellationToken cancellationToken)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            await operation(channel);
            return null;
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode is 403 or 404 or 406)
        {
            return ex.ShutdownReason.ReplyCode;
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

    private async Task<StepResult> CheckRabbitAsync(ExpectedTopology expected, CancellationToken cancellationToken)
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
            var refused = await TryAsync(connection, channel => isQueue
                ? channel.QueueDeclarePassiveAsync(name, cancellationToken)
                : channel.ExchangeDeclarePassiveAsync(name, cancellationToken), cancellationToken);
            if (refused == 404)
                problems.Add($"{label} '{name}' doesn't exist");
            else if (refused is not null)
                problems.Add($"{label} '{name}' is missing or the service may not use it");
        }
        return new StepResult(objects.Count, [], [], problems);
    }

    private async Task<StepResult> ProvisionRabbitAsync(ExpectedTopology expected, MessagingOptions value, CancellationToken cancellationToken)
    {
        var connection = await services.GetRequiredService<RabbitConnection>().GetConnectionAsync(cancellationToken);
        var created = new List<string>();
        var problems = new List<string>();
        var notes = new List<string>();
        var waiting = new List<string>();
        var count = 0;

        Task<ushort?> PassiveAsync(string name, bool isQueue) => TryAsync(connection, channel => isQueue
            ? channel.QueueDeclarePassiveAsync(name, cancellationToken)
            : channel.ExchangeDeclarePassiveAsync(name, cancellationToken), cancellationToken);

        // Creates an object a passive declare didn't find. Refused both ways (403), it may exist for a service
        // with the narrowest rights: left to whoever made it.
        async Task<bool> CreateAsync(string label, string name, ushort? passive, Func<IChannel, Task> declare)
        {
            var refused = await TryAsync(connection, declare, cancellationToken);
            switch (refused)
            {
                case null:
                    created.Add($"{label} '{name}'");
                    return true;
                case 406:
                    problems.Add($"{label} '{name}' exists with other settings");
                    return false;
                case 403 when passive == 403:
                    notes.Add($"{label} '{name}' (may not see or create it)");
                    return true;
                default:
                    problems.Add($"{label} '{name}' couldn't be created (the broker answered {refused}): the service lacks configure on it, or write on its dead-letter exchange");
                    return false;
            }
        }

        async Task<bool> EnsureExchangeAsync(string label, string name, string type)
        {
            count++;
            var passive = await PassiveAsync(name, false);
            return passive is null || await CreateAsync(label, name, passive, channel =>
                channel.ExchangeDeclareAsync(name, type, durable: true, cancellationToken: cancellationToken));
        }

        Task<ushort?> BindAsync(string queue, string exchange, string routingKey) =>
            TryAsync(connection, channel => channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: cancellationToken), cancellationToken);

        // Arguments rather than policies, which a service can't set over AMQP. True when created now, false
        // when it was there, null when it can't be used.
        async Task<bool?> EnsureQueueAsync(string label, string queue)
        {
            count++;
            var passive = await PassiveAsync(queue, true);
            if (passive is null)
                return false;
            var deadLetter = RabbitSubscriber.DeadLetterPrefix + queue;
            if (passive == 404)
            {
                if (!await EnsureExchangeAsync("dead-letter exchange", deadLetter, ExchangeType.Fanout))
                    return null;
                count++;
                var deadLetterPassive = await PassiveAsync(deadLetter, true);
                if (deadLetterPassive is not null && !await CreateAsync("dead-letter queue", deadLetter, deadLetterPassive, channel =>
                        channel.QueueDeclareAsync(deadLetter, durable: true, exclusive: false, autoDelete: false,
                            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: cancellationToken)))
                    return null;
                if (await BindAsync(deadLetter, deadLetter, "") is not null)
                {
                    problems.Add($"dead-letter queue '{deadLetter}' couldn't be bound to its exchange");
                    return null;
                }
            }
            var before = created.Count;
            var usable = await CreateAsync(label, queue, passive, channel =>
                channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?>
                    {
                        ["x-queue-type"] = "quorum",
                        ["x-delivery-limit"] = Math.Max(0, value.MaxDeliveryCount - 1),
                        ["x-dead-letter-exchange"] = deadLetter,
                        ["x-dead-letter-strategy"] = "at-least-once",
                        ["x-overflow"] = "reject-publish",
                    },
                    cancellationToken: cancellationToken));
            return usable ? created.Count > before : null;
        }

        // A queue that existed is bound again in case a release added a family; a refusal there is only a note.
        void Refused(bool queueCreated, string what)
        {
            if (queueCreated)
                problems.Add($"{what} couldn't be made");
            else
                notes.Add(what);
        }

        // Its own topics. Topics it subscribes to are their owners' to create: making them here would need the
        // right to configure, and so to delete, another service's exchange.
        foreach (var topic in expected.Topics)
            await EnsureExchangeAsync("topic exchange", topic, ExchangeType.Topic);
        foreach (var (topic, subscription) in expected.Subscriptions)
        {
            var queue = RabbitSubscriber.QueueName(topic, subscription);
            if (await EnsureQueueAsync("subscription queue", queue) is not { } queueCreated)
                continue;
            foreach (var (_, _, family) in expected.SubscriptionFamilies.Where(f => f.Topic == topic && f.Subscription == subscription))
            {
                var refused = await BindAsync(queue, topic, family);
                if (refused == 404)
                {
                    waiting.Add($"topic exchange '{topic}', which its owner creates");
                    break;
                }
                if (refused is not null)
                    Refused(queueCreated, $"binding of queue '{queue}' to topic exchange '{topic}' for '{family}'");
            }
        }
        foreach (var queue in expected.CommandQueues)
        {
            if (!await EnsureExchangeAsync("command queue exchange", queue, ExchangeType.Direct)
                || await EnsureQueueAsync("command queue", queue) is not { } queueCreated)
                continue;
            if (await BindAsync(queue, queue, queue) is not null)
                Refused(queueCreated, $"binding of command queue '{queue}' to its exchange");
        }

        if (problems.Count == 0 && waiting.Count > 0)
            throw new TopologyNotReadyException($"Waiting for {string.Join("; ", waiting.Distinct())}.");
        return new StepResult(count, [], created, problems) { Notes = notes };
    }

    private async Task<StepResult> CheckAzureServiceBusAsync(ExpectedTopology expected, CancellationToken cancellationToken)
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
        return new StepResult(entities.Count, notChecked, [], problems);
    }

    private async Task<StepResult> ProvisionAzureServiceBusAsync(ExpectedTopology expected, MessagingOptions value, CancellationToken cancellationToken)
    {
        // Managing entities needs the Manage right: a connection string's policy, or the Azure Service Bus Data
        // Owner role for a credential.
        var admin = !string.IsNullOrEmpty(value.AzureServiceBusConnectionString)
            ? new ServiceBusAdministrationClient(value.AzureServiceBusConnectionString)
            : new ServiceBusAdministrationClient(value.AzureServiceBusFullyQualifiedNamespace,
                services.GetRequiredService<AzureServiceBusConnection>().Credential);
        var created = new List<string>();
        var problems = new List<string>();
        var waiting = new List<string>();
        var count = 0;

        // Creates an entity only when it doesn't exist, so one an operator made is left as it is. Another
        // instance creating it at the same moment counts as found.
        async Task EnsureAsync(string label, Func<Task> ensureAsync)
        {
            count++;
            try
            {
                await ensureAsync();
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
            {
            }
            catch (UnauthorizedAccessException)
            {
                problems.Add($"{label} couldn't be created: the service lacks the Manage right");
            }
        }

        foreach (var topic in expected.Topics)
        {
            await EnsureAsync($"topic '{topic}'", async () =>
            {
                if ((await admin.TopicExistsAsync(topic, cancellationToken)).Value)
                    return;
                await admin.CreateTopicAsync(TopicOptions(topic), cancellationToken);
                created.Add($"topic '{topic}'");
            });
        }
        foreach (var (topic, subscription) in expected.Subscriptions)
        {
            var label = $"subscription '{subscription}' on topic '{topic}'";
            var families = expected.SubscriptionFamilies
                .Where(f => f.Topic == topic && f.Subscription == subscription).Select(f => f.Family).ToList();
            await EnsureAsync(label, async () =>
            {
                try
                {
                    if (!(await admin.SubscriptionExistsAsync(topic, subscription, cancellationToken)).Value)
                    {
                        // Its $Default rule, which would admit every family, admits its first family alone.
                        await admin.CreateSubscriptionAsync(SubscriptionOptions(topic, subscription, value.MaxDeliveryCount),
                            new CreateRuleOptions(RuleProperties.DefaultRuleName, FamilyFilter(families[0])), cancellationToken);
                        created.Add(label);
                    }
                }
                catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
                {
                    // Topics it subscribes to are their owners' to create.
                    waiting.Add($"topic '{topic}', which its owner creates");
                    return;
                }

                // A rule for each family no rule admits yet, such as one a release added.
                var filters = new List<RuleFilter>();
                await foreach (var rule in admin.GetRulesAsync(topic, subscription, cancellationToken))
                    filters.Add(rule.Filter);
                foreach (var family in MissingFamilies(filters, families))
                {
                    try
                    {
                        await admin.CreateRuleAsync(topic, subscription, new CreateRuleOptions(family, FamilyFilter(family)), cancellationToken);
                        created.Add($"rule '{family}' on {label}");
                    }
                    catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
                    {
                    }
                }
            });
        }
        foreach (var queue in expected.CommandQueues)
        {
            await EnsureAsync($"command queue '{queue}'", async () =>
            {
                if ((await admin.QueueExistsAsync(queue, cancellationToken)).Value)
                    return;
                await admin.CreateQueueAsync(QueueOptions(queue, value.MaxDeliveryCount), cancellationToken);
                created.Add($"command queue '{queue}'");
            });
        }

        if (problems.Count == 0 && waiting.Count > 0)
            throw new TopologyNotReadyException($"Waiting for {string.Join("; ", waiting.Distinct())}.");
        return new StepResult(count, [], created, problems);
    }

    /// <summary>
    /// The families no rule admits: one with no filter of its own ($Default unchanged) admits every family,
    /// and a correlation filter on the family property alone admits that family.
    /// </summary>
    internal static IEnumerable<string> MissingFamilies(IEnumerable<RuleFilter> filters, IEnumerable<string> families)
    {
        var list = filters.ToList();
        if (list.Any(filter => filter is TrueRuleFilter))
            return [];
        var admitted = list.OfType<CorrelationRuleFilter>()
            .Where(filter => filter.ApplicationProperties.Count == 1
                && filter.ApplicationProperties.TryGetValue(BrokerProperties.PayloadFamily, out var family) && family is string)
            .Select(filter => (string)filter.ApplicationProperties[BrokerProperties.PayloadFamily])
            .ToHashSet(StringComparer.Ordinal);
        return families.Where(family => !admitted.Contains(family));
    }

    // The settings infra/messaging/message-broker.bicep gives each entity in the cloud.

    internal static CreateTopicOptions TopicOptions(string topic) => new(topic)
    {
        // Drops a re-send under the same message identifier.
        RequiresDuplicateDetection = true,
        DuplicateDetectionHistoryTimeWindow = DuplicateDetectionWindow,
    };

    internal static CreateSubscriptionOptions SubscriptionOptions(string topic, string subscription, int maxDeliveryCount) => new(topic, subscription)
    {
        MaxDeliveryCount = maxDeliveryCount,
        LockDuration = LockDuration,
        DeadLetteringOnMessageExpiration = true,
        EnableDeadLetteringOnFilterEvaluationExceptions = true,
    };

    internal static CreateQueueOptions QueueOptions(string queue, int maxDeliveryCount) => new(queue)
    {
        MaxDeliveryCount = maxDeliveryCount,
        LockDuration = LockDuration,
        DeadLetteringOnMessageExpiration = true,
        RequiresDuplicateDetection = true,
        DuplicateDetectionHistoryTimeWindow = DuplicateDetectionWindow,
    };

    internal static CorrelationRuleFilter FamilyFilter(string family) =>
        new() { ApplicationProperties = { [BrokerProperties.PayloadFamily] = family } };
}
