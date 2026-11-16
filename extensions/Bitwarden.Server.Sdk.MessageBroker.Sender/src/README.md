# Bitwarden.Server.Sdk.MessageBroker.Sender

**Audience:** engineers changing this package.

A transactional outbox for `Bitwarden.Server.Sdk.MessageBroker`, on Entity Framework Core. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Writing messages to the outbox with a change, and sending them from it afterwards: registration, the
row's model, the dispatcher, and what it reports. Anything about how a message reaches or leaves a
broker belongs in MessageBroker, which this package reaches through its internals. Nothing here may
depend on a particular database engine: every query must translate on SQL Server, PostgreSQL, MySQL,
MariaDB and SQLite.

## How it works

- **Registration.** `AddOutboxPublisher` and `AddOutboxCommandSender` register, for one message type
  and destination, MessageBroker's own publisher under the internal key
  `MessageBrokerKeys.Direct(key)` (`AddDirectPublisher` and `AddDirectCommandSender`), so a service
  can't inject it; an `OutboxPublisher` under the public key, scoped like the `DbContext` it writes
  to; an `OutboxRegistration` holding the route the dispatcher sends through; and one
  `OutboxDispatcher<TContext>` per context. `AddOutboxLocalHandler` does the same with
  `OutboxLocalRoute` and the reserved destination `local`.
- **One row, three writers.** `OutboxPublisher`, `IOutboxSender` (through the keyed publisher) and
  `MessageOutbox.Add` all build the row with `OutboxRows.Create`. `MessageOutbox.Add` resolves the
  serializer from the context's application service provider, for an interceptor that has no scope.
  `SendLaterAsync` reaches the outbox through the internal `IOutboxPublisher<TPayload>`, which only
  `OutboxPublisher` implements.
- **The stored body is sent as written.** `OutboxBrokerRoute` hands it to the direct publisher's
  internal `SendRawAsync`, so MessageBroker's own sending (publisher confirms, routing, command
  queues, the in-memory backend) applies unchanged and no version a newer build wrote is lost.
- **Claiming.** The dispatcher reads candidate identifiers, then claims them with a conditional
  `ExecuteUpdate` that repeats the eligibility test and stamps a lease token, then reads back the rows
  holding its token. It claims only rows whose destination and type it has a route for. The delete
  and every later update check the token, so a send slower than the lease can't touch a row another
  instance has taken over.
- **Failures.** `OutboxFailures.IsPermanent` decides between retrying and setting aside: MessageBroker's
  internal `SendFailures.IsPermanent`, or an `OutboxMessageSetAsideException` from a route.
  `OutboxLocalRoute` throws that for a body it can't read or a handler that dead-lettered;
  `OutboxEnvelope` records how the handler settled. `LastError` holds the exception's type, never its
  message.
- **Lateness** counts from `DueAt`, which retries don't move. `OutboxBacklog<TContext>` counts the
  table at most once per `BacklogSampleInterval` for both the gauges (`OutboxMetrics`) and the
  health check (`OutboxHealthCheck`).
- **Send on commit.** `OutboxWake<TContext>` and `OutboxSendOnCommitInterceptor<TContext>` are
  registered for every outbox context, and the interceptor reads the option at run time, so they
  are inert while it is off. The interceptor signals after the commit of a save that added outbox
  rows, or straight after a save with no transaction of its own; a rolled-back transaction or a
  failed save signals nothing.
- **The test seam.** `IOutboxSendObserver` (internal) lets a test harness watch what the dispatcher
  hands to the broker, and stand in for a broker that is down. `InternalsVisibleTo` names the
  harness's assembly. No running service registers one.

## Testing

Tests are in `tests/Bitwarden.Server.Sdk.MessageBroker.Sender.Tests`, one class per source file,
on SQLite in memory and the in-memory broker through `OutboxTestHost`. Two more classes need
Docker:

- `OutboxEngineTests` runs the claims, deletes, deferrals, prefix retraction and backlog count on
  SQL Server, PostgreSQL, MySQL and MariaDB, several instances sharing one outbox.
- `BrokerDeliveryTests` sends through a real AMQP broker: RabbitMQ 4.3.6, or the image
  `MESSAGING_TESTS_AMQP_IMAGE` names.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.MessageBroker.Sender/tests/Bitwarden.Server.Sdk.MessageBroker.Sender.Tests
MESSAGING_TESTS_AMQP_IMAGE=cloudamqp/lavinmq:2.10.4 dotnet test --project extensions/Bitwarden.Server.Sdk.MessageBroker.Sender/tests/Bitwarden.Server.Sdk.MessageBroker.Sender.Tests -- --filter-class "*.BrokerDeliveryTests"
```
