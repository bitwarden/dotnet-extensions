using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// The outbox table's model.
/// </summary>
public static class MessageOutbox
{
    /// <summary>
    /// The table's name.
    /// </summary>
    public const string Table = "MessageOutbox";

    /// <summary>
    /// The destination of a message handled in the sending service itself
    /// (<c>AddOutboxLocalHandler</c>). Inject its publisher under <c>"local#{WireName}"</c>.
    /// </summary>
    public const string Local = "local";

    /// <summary>
    /// Adds the outbox table to <paramref name="model"/>.
    /// </summary>
    public static void Apply(ModelBuilder model)
    {
        var row = model.Entity<OutboxMessage>();
        row.ToTable(Table);
        row.HasKey(m => m.MessageId);
        row.Property(m => m.MessageId).HasMaxLength(PublisherExtensions.MaxMessageIdLength).IsUnicode(false);
        row.Property(m => m.Destination).HasMaxLength(260).IsRequired();
        row.Property(m => m.MessageType).HasMaxLength(200).IsRequired();
        row.Property(m => m.Body).IsRequired();
        row.Property(m => m.TraceParent).HasMaxLength(128).IsUnicode(false);
        row.Property(m => m.LastError).HasMaxLength(200);
        row.HasIndex(m => m.AvailableAt);
        row.HasIndex(m => m.DueAt);
        row.HasIndex(m => m.LeaseToken);
    }

    /// <summary>
    /// Adds <paramref name="message"/> to <paramref name="database"/>'s outbox, to be sent to
    /// <paramref name="destination"/> once the context's change is saved: for code that has the
    /// context being saved but not a scope to resolve a publisher in, such as a
    /// <c>SaveChangesInterceptor</c>.
    /// </summary>
    /// <remarks>
    /// The message is serialized as the publisher registered for it serializes it, and the row is
    /// the one its <c>SendAsync</c> would add. The context must have been made by the service's
    /// container, and the type must be registered through the outbox to the destination for this
    /// context (<c>AddOutboxPublisher</c>, <c>AddOutboxCommandSender</c> or, for
    /// <see cref="Local"/>, <c>AddOutboxLocalHandler</c>); otherwise nothing would ever send it.
    /// </remarks>
    /// <typeparam name="TPayload">The message's payload family.</typeparam>
    /// <typeparam name="TCeiling">The family's ceiling.</typeparam>
    /// <param name="database">The context the message commits with.</param>
    /// <param name="destination">The topic or command queue, or <see cref="Local"/>.</param>
    /// <param name="message">The message.</param>
    /// <param name="messageId">Its identifier; a version 7 GUID if not given.</param>
    /// <returns>The message's identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// The context wasn't made by the service's container, or the type isn't registered to the
    /// destination through this context's outbox, or the message is too large.
    /// </exception>
    public static string Add<TPayload, TCeiling>(DbContext database, string destination, TCeiling message, string? messageId = null)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling, Payload<TPayload>.IReachesFloor
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(message);
        messageId ??= Guid.CreateVersion7().ToString();
        PublisherExtensions.ThrowIfInvalidMessageId(messageId);

        var services = database.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                $"{database.GetType().Name} wasn't made by the service's container, so its outbox can't be written to.");
        if (!services.GetServices<OutboxRegistration>().Any(each =>
                each.Context.IsInstanceOfType(database)
                && each.Route.Destination == destination
                && each.Route.MessageType == TPayload.WireName))
        {
            throw new InvalidOperationException(
                $"'{TPayload.WireName}' to '{destination}' isn't registered through {database.GetType().Name}'s outbox, " +
                "so nothing would send it.");
        }

        var variants = new List<Payload<TPayload>.IVariant> { message };
        message.DescendToFloor(variants);
        database.Set<OutboxMessage>().Add(OutboxRows.Create<TPayload>(
            services.GetRequiredKeyedService<IMessageSerializer>(MessageBrokerKeys.Publisher<TPayload>(destination)),
            destination,
            variants,
            messageId,
            notBefore: null,
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetRequiredService<IOptions<MessageOutboxOptions>>().Value.MaxMessageSize));
        return messageId;
    }
}
