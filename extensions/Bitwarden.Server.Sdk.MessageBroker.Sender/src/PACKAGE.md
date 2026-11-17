# Bitwarden.Server.Sdk.MessageBroker.Sender

**Audience:** engineers writing a Bitwarden service that sends events or commands with
`Bitwarden.Server.Sdk.MessageBroker` and keeps its data in a database through Entity Framework Core.

## About

A transactional outbox for `Bitwarden.Server.Sdk.MessageBroker`: a message is sent if, and only if,
the change it belongs to commits, and a broker that is down can't lose it. It depends on
`Bitwarden.Server.Sdk.MessageBroker` and Entity Framework Core's relational provider.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.MessageBroker.Sender
```

## Key concepts

- **The outbox** is a table, `MessageOutbox`, in the service's own database. Sending adds a row to
  it; the caller's `SaveChangesAsync` commits the row with the change, in one transaction. A
  background dispatcher in each instance sends the row afterwards and deletes it once the broker has
  accepted it.
- **Nothing is sent without a save.** `SendAsync` neither sends nor saves. A change that rolls back
  takes its messages with it.
- **At least once.** A message can be sent more than once, always under the same identifier. A
  receiver must treat a repeat as harmless.
- **A destination** is a topic, another service's command queue, or `MessageOutbox.Local`, a
  handler in the sending service itself.
- **Set aside.** A failure that sending again can never fix leaves the row in the table with
  `SetAsideAt` set, for someone to look at. Every other failure is retried until it clears.
- **What a row holds** is the message, serialized: Data at Rest in the sending service's database.
  A message carries identifiers and the minimum facts its receiver needs, never Vault Data,
  Authentication Tokens, key material or other secrets, and the table must be readable only by the
  service that owns it.

## Configuration

`MessageOutboxOptions`, bound by the service however it binds options (for example from the
`MessageOutbox` section).

| Option                  | Default    | What it does                                                         |
| ----------------------- | ---------- | -------------------------------------------------------------------- |
| `Dispatch`              | true       | Whether this instance runs the dispatcher; off, messages wait        |
| `PollInterval`          | 1 second   | The wait between polls when the last batch wasn't full               |
| `BatchSize`             | 100        | Messages claimed per poll                                            |
| `Lease`                 | 30 seconds | How long a claim lasts before another instance may take the message  |
| `MaximumBackoff`        | 5 minutes  | The longest wait between attempts; the wait doubles from two seconds |
| `MaxMessageSize`        | 256 KiB    | The largest serialized message `SendAsync` accepts                   |
| `BacklogSampleInterval` | 30 seconds | How often the outbox is counted for its gauges and health check      |
| `BacklogDegradedAge`    | 10 minutes | How late a message may be before the health check reports degraded   |
| `BacklogUnhealthyAge`   | 1 hour     | How late a message may be before the health check reports unhealthy  |
| `SendOnCommit`          | false      | Wake the dispatcher as soon as a save that wrote messages commits    |

`MaxMessageSize` defaults to Azure Service Bus Standard's limit; raise it on a broker that allows
more. A message handled in the service itself never reaches a broker and has no limit.

`SendOnCommit` suits a service whose readers expect a message's effect within moments of the change.
It wakes only the instance that saved; the poll still runs as the backstop.

## Usage

### Add the outbox to a database

Mark the service's `DbContext` and add the table to its model, then add a migration for the
`MessageOutbox` table on each database engine the service supports.

`OrganizationDbContext.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker.Sender;
using Microsoft.EntityFrameworkCore;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options)
    : DbContext(options), IMessageOutboxContext
{
    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        MessageOutbox.Apply(modelBuilder);
    }
}
```

### Register what the service sends

Register each message type with the topic or command queue it goes to. These take the place of
MessageBroker's `AddPublisher` and `AddCommandSender`. Registering a type through the outbox after
`AddPublisher` or `AddCommandSender` registered it for the same destination throws, since a send
straight to the broker could be lost.

In `Program.cs`:

```csharp
builder.Services.AddOutboxPublisher<OrganizationDeletedPayload, OrganizationDeleted, OrganizationDbContext>("organization.events");
builder.Services.AddOutboxCommandSender<SendMailPayload, SendMail, OrganizationDbContext>("mail.commands");
```

A command sent before its queue exists waits in the outbox until it does.

### Send a message with a change

Inject the same keyed `Publisher<TPayload, TCeiling>` as with MessageBroker, send, then save.

`OrganizationDeletion.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker;
using Microsoft.Extensions.DependencyInjection;

public sealed class OrganizationDeletion(
    OrganizationDbContext database,
    [FromKeyedServices("organization.events#organization.deleted")]
    Publisher<OrganizationDeletedPayload, OrganizationDeleted> deleted)
{
    public async Task DeleteAsync(Organization organization, CancellationToken cancellationToken)
    {
        database.Organizations.Remove(organization);
        await deleted.Publish(new OrganizationDeleted(organization.Id)).SendAsync(cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }
}
```

Pass an identifier to `SendAsync` to choose it; otherwise a version 7 GUID is used.

### Send without naming the destination

`IOutboxSender`, scoped, finds the one destination a message type was registered to go to through
the outbox, and refuses a type registered to none or to several. It takes an optional identifier and
an optional `notBefore`, and returns the identifier.

`OrganizationDeletionBySender.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker.Sender;

public sealed class OrganizationDeletionBySender(OrganizationDbContext database, IOutboxSender outbox)
{
    public async Task DeleteAsync(Organization organization, CancellationToken cancellationToken)
    {
        database.Organizations.Remove(organization);
        await outbox.SendAsync<OrganizationDeletedPayload, OrganizationDeleted>(
            new OrganizationDeleted(organization.Id), cancellationToken: cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }
}
```

It sends a family's ceiling and every older version that plain downcasts reach. A family whose
versions need data from the sender to step down is sent with its keyed publisher.

### Send from a save interceptor

Code that holds the `DbContext` being saved but no scope to resolve a publisher in adds a message
with `MessageOutbox.Add`. It refuses a context the service's container didn't make, or a type not
registered to the destination through that context's outbox.

`OrganizationDeletedInterceptor.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker.Sender;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

public sealed class OrganizationDeletedInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var database = eventData.Context!;
        foreach (var entry in database.ChangeTracker.Entries<Organization>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            MessageOutbox.Add<OrganizationDeletedPayload, OrganizationDeleted>(
                database, "organization.events", new OrganizationDeleted(entry.Entity.Id));
        }
        return ValueTask.FromResult(result);
    }
}
```

### Send later, and take a message back

`SendLaterAsync` adds a message that the dispatcher holds until a given time, and returns its
identifier. `RetractMessageAsync` takes a message back if it hasn't been sent. Together they make a
backstop: send a message for later, do the work at once, and retract the message once the work is
done. If the instance stops first, the message is still sent.

`InvitationAcceptance.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker;
using Bitwarden.Server.Sdk.MessageBroker.Sender;
using Microsoft.Extensions.DependencyInjection;

public sealed class InvitationAcceptance(
    OrganizationDbContext database,
    [FromKeyedServices("organization.events#invitation.accepted")]
    Publisher<InvitationAcceptedPayload, InvitationAccepted> accepted)
{
    public async Task AcceptAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var backstop = await accepted.Publish(new InvitationAccepted(userId, organizationId))
            .SendLaterAsync(DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        await FinishAcceptingAsync(userId, organizationId, cancellationToken);
        await database.RetractMessageAsync(backstop, cancellationToken);
    }

    private Task FinishAcceptingAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
```

`RetractMessageAsync` drops a message added in the current unit of work and not yet saved, and
deletes one already saved at once, inside the caller's transaction if one is open. It returns
`false` once the message has been sent. A send already under way can't be recalled, so a retracted
message can still arrive. Only a publisher registered through the outbox can send later.

### Take back a subject's earlier messages

`RetractMessagesAsync(messageType, idPrefix, except)` takes back every waiting message of a type
whose identifier starts with a prefix, but one: the earlier sends of something only the latest of
which matters. Choose identifiers as `"{subject}.{unique}"` to find a subject's messages by it.

`DeviceRegistration.cs`:

```csharp
using Bitwarden.Server.Sdk.MessageBroker.Sender;

public sealed class DeviceRegistration(OrganizationDbContext database, IOutboxSender outbox)
{
    public async Task RegisterAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var id = await outbox.SendAsync<DeviceRegisteredPayload, DeviceRegistered>(
            new DeviceRegistered(deviceId, DateTimeOffset.UtcNow), $"{deviceId:N}.{Guid.CreateVersion7()}",
            cancellationToken: cancellationToken);
        await database.RetractMessagesAsync(DeviceRegisteredPayload.WireName, $"{deviceId:N}.", except: id, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }
}
```

End the prefix with the separator, so one subject's prefix is never another's start, and never put
a person's data in an identifier.

### Handle a message in the sending service

Work that must follow a change once it commits, but needn't happen inside the request, can be
handed to the service itself: register a handler, and the dispatcher runs it in a scope of its own
instead of sending the message to the broker.

In `Program.cs`:

```csharp
builder.Services.AddOutboxLocalHandler<WelcomeMailPayload, WelcomeMail, OrganizationDbContext, WelcomeMailHandler>();
```

Send it with the publisher keyed `"local#{WireName}"` (`MessageOutbox.Local` is `"local"`), or with
`IOutboxSender`, and save. The handler is the same `IMessageConsumer<TPayload, TCeiling>` a
subscription uses, and is settled the same way:

- **Returns:** the message is complete and its row is deleted.
- **Throws, or calls `RequeueAsync`:** the message is tried again later, with the outbox's backoff.
  `DeliveryCount` counts the attempts.
- **Calls `DeadLetterAsync`:** the message is set aside.

A stored message the handler's version can't read is set aside without calling it. A handler can
run more than once for one message, so it must be harmless to repeat.

### Watch the outbox

Every outbox registers a health check, `Message outbox ({context})`, tagged `monitoring` so a host
reports it without taking instances out of rotation. It is:

- **degraded** once the longest-waiting due message is `BacklogDegradedAge` late;
- **unhealthy** once it is `BacklogUnhealthyAge` late, or any message has been set aside.

Lateness counts from when a message was due: when it was written, or the time given to
`SendLaterAsync`. A message waiting for a later time isn't late, and retries don't reset it.

A set-aside message stays in the table, and the check unhealthy, until someone decides what to do
with it. Its `LastError` names the failure's type, never its message, which could carry the
message's contents. To send it again once the cause is fixed, clear `SetAsideAt` and make it due; to
give it up, delete the row:

```sql
UPDATE MessageOutbox SET SetAsideAt = NULL, AvailableAt = 0 WHERE MessageId = '…';
DELETE FROM MessageOutbox WHERE MessageId = '…';
```

The meter `Bitwarden.Server.Sdk.MessageBroker.Sender` has:

| Instrument                            | Kind      | What it measures                                      |
| ------------------------------------- | --------- | ----------------------------------------------------- |
| `bitwarden.outbox.sent.messages`      | Counter   | Messages sent, or handled in the service, and removed |
| `bitwarden.outbox.send.failures`      | Counter   | Failed attempts, tagged `error.type`                  |
| `bitwarden.outbox.set_aside.messages` | Counter   | Messages set aside                                    |
| `bitwarden.outbox.send.delay`         | Histogram | Seconds a message waited past when it was due         |
| `bitwarden.outbox.pending.messages`   | Gauge     | Messages due and not yet sent                         |
| `bitwarden.outbox.oldest_pending.age` | Gauge     | Seconds the longest-waiting due message is late       |
| `bitwarden.outbox.set_aside.pending`  | Gauge     | Messages set aside and still in the table             |

Counters and the histogram are tagged with `bitwarden.outbox.database` (the context's name),
`messaging.destination.name` and `messaging.payload.family`; the gauges with the database only.
Every instance counts the same table, so aggregate the gauges with `max`, not `sum`.

## Gotchas and constraints

- A message larger than `MaxMessageSize` is refused by `SendAsync`, failing the caller's change.
  Send identifiers, not contents.
- A failure is retried until it clears. Only a message Azure Service Bus reports as too large, or a
  local message its handler dead-lettered or can't read, is set aside.
- During a rolling deploy an instance leaves alone any row of a type it doesn't send, for an
  instance that does.
- With `Dispatch` off, an instance writes to the outbox but sends nothing; another instance must
  dispatch.
- Keep each send and local handler well inside `Lease`: one that runs past it can be sent again by
  another instance.
