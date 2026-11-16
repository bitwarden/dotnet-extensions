using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender.Tests;

public sealed record OrderPlaced(int OrderId) : OrderPlacedPayload.ISole;

public class OrderPlacedPayload : Payload<OrderPlacedPayload, OrderPlaced, OrderPlaced>, IPayloadVariants<OrderPlacedPayload>, IPayloadWireName<OrderPlacedPayload>
{
    public static string WireName => "order.placed";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(OrderPlaced), "v1")];
}

public sealed record SendReceipt(int OrderId) : SendReceiptPayload.ISole;

public class SendReceiptPayload : Payload<SendReceiptPayload, SendReceipt, SendReceipt>, IPayloadVariants<SendReceiptPayload>, IPayloadWireName<SendReceiptPayload>
{
    public static string WireName => "send.receipt";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(SendReceipt), "v1")];
}

public sealed class Order
{
    public int Id { get; set; }
}

public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options), IMessageOutboxContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => MessageOutbox.Apply(modelBuilder);
}

/// <summary>
/// What the receiving side saw.
/// </summary>
public sealed class Received
{
    public ConcurrentQueue<(string Type, int OrderId, string MessageId)> Messages { get; } = new();

    public async Task<(string Type, int OrderId, string MessageId)> WaitForAsync(int orderId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var message in Messages)
                if (message.OrderId == orderId) return message;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"Order {orderId} was never received.");
    }
}

public sealed class OrderPlacedConsumer(Received received) : IMessageConsumer<OrderPlacedPayload, OrderPlaced>
{
    public Task HandleAsync(Envelope<OrderPlacedPayload, OrderPlaced> envelope, CancellationToken cancellationToken)
    {
        received.Messages.Enqueue((OrderPlacedPayload.WireName, envelope.Payload.OrderId, envelope.MessageId));
        return Task.CompletedTask;
    }
}

public sealed class SendReceiptConsumer(Received received) : IMessageConsumer<SendReceiptPayload, SendReceipt>
{
    public Task HandleAsync(Envelope<SendReceiptPayload, SendReceipt> envelope, CancellationToken cancellationToken)
    {
        received.Messages.Enqueue((SendReceiptPayload.WireName, envelope.Payload.OrderId, envelope.MessageId));
        return Task.CompletedTask;
    }
}

public sealed record ReserveStock(int OrderId) : ReserveStockPayload.ISole;

public class ReserveStockPayload : Payload<ReserveStockPayload, ReserveStock, ReserveStock>, IPayloadVariants<ReserveStockPayload>, IPayloadWireName<ReserveStockPayload>
{
    public static string WireName => "reserve.stock";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(ReserveStock), "v1")];
}

/// <summary>
/// What the handler in the sending service saw, and what it does next.
/// </summary>
public sealed class StockReservations
{
    public ConcurrentQueue<(int OrderId, string MessageId, int DeliveryCount)> Handled { get; } = new();

    public Func<Envelope<ReserveStockPayload, ReserveStock>, Task>? Next { get; set; }
}

public sealed class ReserveStockHandler(StockReservations reservations) : IMessageConsumer<ReserveStockPayload, ReserveStock>
{
    public Task HandleAsync(Envelope<ReserveStockPayload, ReserveStock> envelope, CancellationToken cancellationToken)
    {
        reservations.Handled.Enqueue((envelope.Payload.OrderId, envelope.MessageId, envelope.DeliveryCount));
        return reservations.Next?.Invoke(envelope) ?? Task.CompletedTask;
    }
}

/// <summary>
/// A type no test registers through the outbox.
/// </summary>
public sealed record RefundIssued(int OrderId) : RefundIssuedPayload.ISole;

public class RefundIssuedPayload : Payload<RefundIssuedPayload, RefundIssued, RefundIssued>, IPayloadVariants<RefundIssuedPayload>, IPayloadWireName<RefundIssuedPayload>
{
    public static string WireName => "refund.issued";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(RefundIssued), "v1")];
}

/// <summary>
/// Another payload under <see cref="SendReceiptPayload"/>'s name.
/// </summary>
public sealed record ImpostorReceipt(string Note) : ImpostorReceiptPayload.ISole;

public class ImpostorReceiptPayload : Payload<ImpostorReceiptPayload, ImpostorReceipt, ImpostorReceipt>, IPayloadVariants<ImpostorReceiptPayload>, IPayloadWireName<ImpostorReceiptPayload>
{
    public static string WireName => SendReceiptPayload.WireName;
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(ImpostorReceipt), "v1")];
}

public sealed class ImpostorReceiptConsumer : IMessageConsumer<ImpostorReceiptPayload, ImpostorReceipt>
{
    public Task HandleAsync(Envelope<ImpostorReceiptPayload, ImpostorReceipt> envelope, CancellationToken cancellationToken) => Task.CompletedTask;
}
