# Evolving payloads

**Audience:** Application developers who own a payload family and need to add, retire, or change a
variant without breaking downstream services.

A payload is a chain of variants from floor (oldest known shape) to ceiling (newest). Publishers
send every known variant of a payload in a single message. Each subscriber picks the highest
variant it can decode and walks pure upcasts back up to its declared ceiling. Old subscribers keep
working against new publishers while the old variant is still on the wire, and new subscribers
keep working against old publishers while the older ceiling can upcast to the newer one.

The service that publishes a topic owns its payload type and its `Variants` list. Package the
payload type in a library so subscribing services can reference it to establish the contract.

## Variant interfaces at a glance

Variants form the chain by implementing one of the interfaces nested under `Payload<TSelf>`. Pick
the interface that matches the variant's position in the chain and the shape of its downcast:

| Position                | Pure downcast                                                                                           | Assisted downcast                          |
| ----------------------- | ------------------------------------------------------------------------------------------------------- | ------------------------------------------ |
| Floor (oldest)          | `IFloor<TUp>`                                                                                           | n/a — the floor has no downcast            |
| Middle                  | `IPure<TDown, TUp>` (usable only until the first assisted above the floor, then use `IPureAboveAssist`) | `IAssisted<TDown, TAssist, TUp, TSelfV>`   |
| Middle above an assist  | `IPureAboveAssist<TDown, TBlocked, TUp>`                                                                | n/a — above-assist implies a pure step     |
| Ceiling (newest)        | `IPureCeiling<TDown>` (usable only if all downcasts are pure)                                           | `IAssistedCeiling<TDown, TAssist, TSelfV>` |
| Ceiling above an assist | `IPureAboveAssistCeiling<TDown, TBlocked>`                                                              | n/a                                        |
| Single-variant family   | `ISole`                                                                                                 | n/a                                        |

`IAssist<TFrom, TTo, TSelfA>` is the matching marker for the caller-supplied data an assisted
downcast consumes. The variant names its assist type and the assist names its variant pair, so
neither compiles unless the other agrees.

## Adding a variant (unassisted)

Say `OrganizationDeleted` carries only the organization id today and needs to grow a deletion
reason.

Before:

```csharp
public sealed class OrganizationDeletedPayload
    : Payload<OrganizationDeletedPayload, OrganizationDeletedPayload.V1, OrganizationDeletedPayload.V1>,
      IPayloadVariants<OrganizationDeletedPayload>,
      IPayloadWireName<OrganizationDeletedPayload>
{
    public static string WireName => "organization.deleted";
    public static IReadOnlyList<(Type Type, string WireName)> Variants =>
    [
        (typeof(V1), "v1"),
    ];

    public sealed record V1(Guid OrganizationId)
        : ISole;
}
```

After:

```csharp
public sealed class OrganizationDeletedPayload
    : Payload<OrganizationDeletedPayload, OrganizationDeletedPayload.V1, OrganizationDeletedPayload.V2>,
      IPayloadVariants<OrganizationDeletedPayload>,
      IPayloadWireName<OrganizationDeletedPayload>
{
    public static string WireName => "organization.deleted";
    public static IReadOnlyList<(Type Type, string WireName)> Variants =>
    [
        (typeof(V1), "v1"),
        (typeof(V2), "v2"),
    ];

    public sealed record V1(Guid OrganizationId)
        : IFloor<V2>
    {
        public V2 Upcast() => new(OrganizationId, Reason: "unknown");
    }

    public sealed record V2(Guid OrganizationId, string Reason)
        : IPureCeiling<V1>
    {
        public V1 Downcast() => new(OrganizationId);
    }
}
```

The upcast on V1 fills the new field with a safe default so a V1 message from an older publisher
still produces a well-formed V2 for a V2-ceiling consumer. The downcast on V2 is pure because V1's
fields are a strict subset of V2's.

Update the publisher registration to the new ceiling —
`AddPublisher<OrganizationDeletedPayload, OrganizationDeletedPayload.V2>("org-deleted")` — and
ship. Existing subscribers keep working at V1 and move to V2 whenever they're ready.

## Adding a variant whose downcast needs new data (assisted)

The pure-downcast pattern above works only when every field V2 adds has a safe default — the V2
`Downcast()` can throw the new data away. When the old subscriber actually needs the new data to
stay on V1, the downcast cannot be pure. The caller supplies the missing piece at publish time,
and the compiler enforces it.

Say `OrderPlaced.V2` removes a `ShippingAddressId` that V1 subscribers rely on to look up the
shipping address — the address is not derivable from V2's other fields, and dropping it on
downcast would break those subscribers.

```csharp
public sealed class OrderPlacedPayload
    : Payload<OrderPlacedPayload, OrderPlacedPayload.V1, OrderPlacedPayload.V2>,
      IPayloadVariants<OrderPlacedPayload>,
      IPayloadWireName<OrderPlacedPayload>
{
    public static string WireName => "order.placed";
    public static IReadOnlyList<(Type Type, string WireName)> Variants =>
    [
        (typeof(V1), "v1"),
        (typeof(V2), "v2"),
    ];

    // The assist carries the datum that V2 → V1 needs. The record names its (From, To, Self)
    // triple so a .With(assist) call unambiguously identifies which downcast it is crossing.
    public sealed record ShippingAddressAssist(Guid ShippingAddressId)
        : Payload<OrderPlacedPayload>.IAssist<V2, V1, ShippingAddressAssist>;

    public sealed record V1(Guid OrderId, Guid ShippingAddressId)
        : IFloor<V2>
    {
        public V2 Upcast() => new(OrderId, CustomerId: null);
    }

    public sealed record V2(Guid OrderId, Guid? CustomerId)
        : IAssistedCeiling<V1, ShippingAddressAssist, V2>
    {
        public V1 Downcast(ShippingAddressAssist assist) =>
            new(OrderId, assist.ShippingAddressId);
    }
}
```

At the publish site, chain one `.With(assist)` per assisted downcast between the ceiling and the
floor:

```csharp
await publisher
    .Publish(new OrderPlacedPayload.V2(orderId, customerId))
    .With(new OrderPlacedPayload.ShippingAddressAssist(shippingAddressId))
    .SendAsync(cancellationToken);
```

`SendAsync` only compiles once every assisted downcast has been crossed, so omitting the assist is
a build error, not a runtime failure. The publisher cannot ship without supplying the data an
older subscriber needs.

### Why upcasts are always pure

Only the publisher has the context to fill in new fields. A publisher that mutates V2 to produce
the wire message can reach into the handler that produced V2 (the request, the database, whatever
else) to supply what the V1 -> V2 downcast needs. A subscriber walking V1 up to V2 has only the wire
bytes and its own process state, so there is no caller-supplied data to inject.

## Retiring a variant (breaking)

In this system, removing a variant is the only breaking change. Breaking changes do not apply to the
in-memory channel backend, since both publisher and subscriber ends use identical payload type libraries.

Removing V1 breaks at consume time. A subscriber still built against a payload type that
references V1 — one running an older copy of the shared type library — can no longer decode
post-retirement messages, and the transport dead-letters the message before either
`IMessageConsumer<...>` or `ISubscriber<...>` receive it.

Before removing V1, use the `messaging.variant.name` tag on `messaging.client.consumed.messages`,
filtered by `messaging.payload.family = "organization.deleted"`, to confirm no subscriber is still
reading it. V1 falling to zero across the deployment is the signal it's safe to drop. Monitor the
dead-letter queue after the removal ships to catch any straggling subscriber.

Once V1 is cold, the V1+V2 state from above collapses to:

```csharp
public sealed class OrganizationDeletedPayload
    : Payload<OrganizationDeletedPayload, OrganizationDeletedPayload.V2, OrganizationDeletedPayload.V2>,
      IPayloadVariants<OrganizationDeletedPayload>,
      IPayloadWireName<OrganizationDeletedPayload>
{
    public static string WireName => "organization.deleted";
    public static IReadOnlyList<(Type Type, string WireName)> Variants =>
    [
        (typeof(V2), "v2"),
    ];

    public sealed record V2(Guid OrganizationId, string Reason)
        : Payload<OrganizationDeletedPayload>.ISole;
}
```
