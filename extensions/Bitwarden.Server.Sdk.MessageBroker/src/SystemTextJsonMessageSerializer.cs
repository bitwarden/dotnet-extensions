using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Wire format is a JSON array of variants with an inline discriminator:
/// <c>[{"$type":"&lt;FullName&gt;", ...variant fields}]</c>. SystemTextJson's polymorphism support does the
/// tagging and dispatch — the derived-type table is enumerated from
/// <see cref="IPayloadVariants{TSelf}.Variants"/> at first use. Unknown discriminators are skipped
/// so a subscriber can survive a publisher with a different set of versions. Any other malformation surfaces as
/// <see cref="JsonException"/>.
/// </summary>
internal sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _userOptions;
    private readonly ConcurrentDictionary<Type, JsonSerializerOptions> _optionsByPayload = new();

    public SystemTextJsonMessageSerializer(IOptionsMonitor<MessageBrokerSerializerOptions> optionsMonitor, string name)
    {
        _userOptions = optionsMonitor.Get(name).JsonSerializerOptions;
        if (_userOptions.TypeInfoResolver is null)
        {
            throw new InvalidOperationException(
                $"No TypeInfoResolver is configured for message broker queue '{name}'. " +
                $"When reflection is disabled, configure a JsonSerializerContext via " +
                $"services.Configure<{nameof(MessageBrokerSerializerOptions)}>(name, o => o.JsonSerializerOptions.TypeInfoResolverChain.Add(MyContext.Default)).");
        }
    }

    public void SerializeVariants<TPayload>(
        IEnumerable<Payload<TPayload>.IVariant> variants,
        IBufferWriter<byte> destination)
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>
    {
        var typeInfo = GetVariantTypeInfo<TPayload>();
        using var writer = new Utf8JsonWriter(destination);
        writer.WriteStartArray();
        foreach (var variant in variants)
            JsonSerializer.Serialize(writer, variant, typeInfo);
        writer.WriteEndArray();
    }

    public IReadOnlyList<Payload<TPayload>.IVariant> DeserializeVariants<TPayload>(ReadOnlySpan<byte> source)
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>
    {
        var typeInfo = GetVariantTypeInfo<TPayload>();

        using var doc = JsonDocument.Parse(source.ToArray());
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a JSON array of variants.");

        var result = new List<Payload<TPayload>.IVariant>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            // A null result means the wire's $type discriminator is not in TPayload.Variants —
            // a publisher sent a variant this subscriber has not been rebuilt against.
            var variant = element.Deserialize(typeInfo);
            if (variant is not null) result.Add(variant);
        }
        return result;
    }

    private JsonTypeInfo<Payload<TPayload>.IVariant> GetVariantTypeInfo<TPayload>()
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>
    {
        var options = _optionsByPayload.GetOrAdd(typeof(TPayload), _ => BuildOptions<TPayload>());
        return (JsonTypeInfo<Payload<TPayload>.IVariant>)options.GetTypeInfo(typeof(Payload<TPayload>.IVariant));
    }

    private JsonSerializerOptions BuildOptions<TPayload>()
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>
    {
        var clone = new JsonSerializerOptions(_userOptions);
        var polymorphic = new PolymorphicVariantResolver(typeof(Payload<TPayload>.IVariant), TPayload.Variants);
        clone.TypeInfoResolver = JsonTypeInfoResolver.Combine(polymorphic, clone.TypeInfoResolver);
        return clone;
    }
}

/// <summary>
/// Resolves a polymorphism-configured <see cref="JsonTypeInfo"/> for one variant interface and
/// delegates every other type to the next resolver in the chain. The derived-type table is fixed
/// at construction; concrete variants are still served by the user's source-gen context.
/// </summary>
internal sealed class PolymorphicVariantResolver(
    Type variantInterface,
    IReadOnlyList<(Type Type, string WireName)> variants)
    : IJsonTypeInfoResolver
{
    private readonly Type _variantInterface = variantInterface;
    private readonly IReadOnlyList<(Type Type, string WireName)> _variants = variants;
    private readonly Lock _lock = new();
    private JsonTypeInfo? _cached;

    // JsonTypeInfo.CreateJsonTypeInfo is marked RequiresDynamicCode, but here it is only used to
    // build a polymorphism umbrella over an interface — SystemTextJson never introspects the interface for
    // members. Each derived variant is served by the user's own source-gen context via the
    // resolver chain, so AOT / trimming remain valid end-to-end.
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "CreateJsonTypeInfo is used only to attach polymorphism metadata to an interface; concrete variants come from the user's source-gen context.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "CreateJsonTypeInfo is used only to attach polymorphism metadata to an interface; concrete variants come from the user's source-gen context.")]
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        if (type != _variantInterface) return null;

        // A resolver may be called with any options instance, but this one is bound to a specific
        // per-TPayload clone the serializer built. Cache once and hand back the same JsonTypeInfo.
        lock (_lock)
        {
            if (_cached is not null) return _cached;
            var info = JsonTypeInfo.CreateJsonTypeInfo(_variantInterface, options);
            var poly = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$type",
                IgnoreUnrecognizedTypeDiscriminators = true,
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
            };
            foreach (var (variantType, wireName) in _variants)
                poly.DerivedTypes.Add(new JsonDerivedType(variantType, wireName));
            info.PolymorphismOptions = poly;
            _cached = info;
            return info;
        }
    }
}
