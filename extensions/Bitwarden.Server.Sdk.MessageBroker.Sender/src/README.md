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

This section is how the code does it; what a service sees (retries, set-asides, lateness, send on
commit) is in [PACKAGE.md](PACKAGE.md).

- **Registration.** `AddOutboxPublisher` and `AddOutboxCommandSender` register, for one message type
  and destination, MessageBroker's own publisher under the internal key
  `MessageBrokerKeys.Direct(key)` (`AddDirectPublisher` and `AddDirectCommandSender`), so a service
  can't inject it; an `OutboxPublisher` under the public key, scoped like the `DbContext` it writes
  to; an `OutboxRegistration` holding the route the dispatcher sends through; and one
  `OutboxDispatcher<TContext>` per context. `AddOutboxLocalHandler` does the same with
  `OutboxLocalRoute` and the reserved destination `local`, and registers that destination's
  serializer with MessageBroker's `AddFamilySerializer`.
- **One row, three writers.** `OutboxPublisher`, `IOutboxSender` (through the keyed publisher) and
  `MessageOutbox.Add` all build the row with `OutboxRows.Create`. `MessageOutbox.Add` resolves the
  serializer from the context's application service provider, for an interceptor that has no scope.
  `SendLaterAsync` reaches the outbox through the internal `IOutboxPublisher<TPayload>`, which only
  `OutboxPublisher` implements.
- **Sending a stored body.** `OutboxBrokerRoute` hands the row's body to the direct publisher's
  internal `SendRawAsync`, so MessageBroker's own sending applies and nothing is decoded.
- **Claiming.** The dispatcher's filter is one `(Destination, MessageType)` term per route, OR-ed
  into an expression so it becomes SQL on every engine. It reads candidate identifiers, claims them
  with a conditional `ExecuteUpdate` that repeats the eligibility test and stamps a lease token,
  then reads back the rows holding its token. The delete and every later update check the token.
  Before each send it checks the time against the lease less a margin (`LeaseMargin`, a quarter of
  the lease) and releases the rest of the batch once past it. A claimed row with no route, which the
  filter shouldn't let through, is released with its `AvailableAt` pushed two seconds on.
  `DispatchAsync` returns the number of routed rows, which decides whether the next poll waits.
- **Failures.** `OutboxFailures.IsPermanent` decides between deferring and setting aside: it is true
  for MessageBroker's internal `SendFailures.IsPermanent` and for an
  `OutboxMessageSetAsideException`. `OutboxLocalRoute` throws that exception for a body it can't
  read or a handler that dead-lettered, reading the outcome `OutboxEnvelope` recorded.
- **The backlog.** `OutboxBacklog<TContext>` caches its last count for `BacklogSampleInterval`; the
  dispatcher refreshes it after each poll, and `OutboxMetrics` and `OutboxHealthCheck` read it.
- **Send on commit.** `OutboxWake<TContext>` and `OutboxSendOnCommitInterceptor<TContext>` are
  registered once per outbox context, the interceptor through `ConfigureDbContext`. The interceptor
  reads the option at run time and tracks pending contexts in a weak table.
- **The test seam.** `IOutboxSendObserver` (internal) lets a test harness watch what the dispatcher
  hands to the broker, and stand in for a broker that is down. No running service registers one.

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
