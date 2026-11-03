# Jobs

The standard requires an API that accepts work to be completed later to answer `202 Accepted` with
a job, a resource representing the accepted work and its progress. It does not yet define the job
resource: its fields, its states, or how a caller follows it. The package provides no asynchronous
operations and no job resource. The standard:
[Jobs](https://contributing.bitwarden.com/architecture/server/apis/jobs).

## What the package does

| The standard                                                                      | The package                                                                                                                                    |
| --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Work accepted for later answers `202 Accepted` with a job (`MUST`)                | Not provided. No standard attribute documents `202`, and there is no job type or job endpoint.                                                 |
| Applies to creates, updates, deletes, actions and bulk operations alike           | Every standard operation answers when its work is done. See [Synchronous and asynchronous](./bulk-operations/synchronous-and-asynchronous.md). |
| The job resource, its fields, states and how a caller follows it                  | Not yet defined by the standard, so not provided.                                                                                              |
| Prefer events on the messaging plane for service-to-service work that only reacts | Outside this package.                                                                                                                          |

Bitwarden.Server.Sdk.Jobs is a different thing: it runs recurring background work on a schedule
inside a service. It has no API and is not a job resource in this sense.

## What you decide

- **Keep operations synchronous.** Bound each operation's work (for a bulk operation, its maximum
  with `BulkRequests.RequireAtMost`) so it can answer while the caller waits, and say in its
  documentation that it responds synchronously.
- **Don't publish your own job shape.** Until the standard defines the job resource, a
  hand-made one becomes a contract every caller depends on and that later has to change. If an
  operation truly cannot be bounded, raise it with the owners of the API standards before building
  one.
- **React with events, not polling.** Where the later work is another service responding to a
  change, publish an event for it to consume rather than offering an asynchronous API.

## See also

- [Synchronous and asynchronous](./bulk-operations/synchronous-and-asynchronous.md)
- [Bulk operations](./bulk-operations/index.md)
- The standard: [Jobs](https://contributing.bitwarden.com/architecture/server/apis/jobs) and
  [Synchronous and asynchronous](https://contributing.bitwarden.com/architecture/server/apis/bulk-operations/synchronous-and-asynchronous)
