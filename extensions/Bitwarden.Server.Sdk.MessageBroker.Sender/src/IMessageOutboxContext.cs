using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Marks a <see cref="DbContext"/> whose database holds the service's outbox. Call
/// <see cref="MessageOutbox.Apply"/> from its <c>OnModelCreating</c> and add a migration for the table.
/// </summary>
public interface IMessageOutboxContext;
