using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Reflection helpers that inspect a variant's implemented <c>Payload&lt;T&gt;.I*</c> interfaces to
/// find its immediate neighbours in the chain. Used at DI time by <see cref="ChainValidator{TPayload, TCeiling}"/>
/// and at runtime by <see cref="Envelope{TPayload, TCeiling}"/>. Variants are pinned by
/// <c>typeof()</c> references in <see cref="IPayloadVariants{TSelf}.Variants"/>, so their
/// interface tables survive trimming.
/// </summary>
internal static class ChainWalk
{
    /// <summary>
    /// The variant <paramref name="variant"/> upcasts to, or <see langword="null"/> if it
    /// declares no Upcast (i.e., is the ceiling).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Variant types are pinned by typeof() references in IPayloadVariants.Variants, so their interface tables are preserved.")]
    public static Type? NextInChain(Type variant)
    {
        foreach (var iface in variant.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(Payload<>.IPureUp<>))
                return iface.GetGenericArguments()[1];
        }
        return null;
    }

    // GetGenericArguments on a nested generic includes the outer type's parameters first
    // (Payload<TSelf>'s TSelf), then this interface's own — so the TDown/TUp sits at args[1].
    /// <summary>
    /// The variant <paramref name="variant"/> downcasts to, or <see langword="null"/> if it
    /// declares no Downcast (i.e., is the floor).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Variant types are pinned by typeof() references in IPayloadVariants.Variants, so their interface tables are preserved.")]
    public static Type? PreviousInChain(Type variant)
    {
        foreach (var iface in variant.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            var def = iface.GetGenericTypeDefinition();
            if (def == typeof(Payload<>.IPure<,>)
                || def == typeof(Payload<>.IPureCeiling<>)
                || def == typeof(Payload<>.IPureAboveAssist<,,>)
                || def == typeof(Payload<>.IPureAboveAssistCeiling<,>)
                || def == typeof(Payload<>.IAssistedDowncast<,,>))
                return iface.GetGenericArguments()[1];
        }
        return null;
    }
}
