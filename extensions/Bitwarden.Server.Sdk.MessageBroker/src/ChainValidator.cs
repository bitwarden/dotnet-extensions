namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Validates <typeparamref name="TPayload"/>'s variant chain shape at DI registration time.
/// </summary>
internal static class ChainValidator<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private static readonly AggregateException? _error = ValidateChain();

    /// <summary>Throws if any problem was found in <typeparamref name="TPayload"/>'s chain.</summary>
    public static void ThrowIfInvalid()
    {
        if (_error is not null) throw _error;
    }

    private static AggregateException? ValidateChain()
    {
        var errors = new List<Exception>();
        var declared = TPayload.Variants;

        if (declared.Count == 0)
        {
            errors.Add(new InvalidOperationException(
                $"{typeof(TPayload).Name} declares no variants. A payload must have at least one variant."));
            return Wrap(errors);
        }

        var variants = declared.Select(v => v.Type).ToArray();

        // === Phase 1: shape enumeration ===
        // Duplicate type check.
        var indexByType = new Dictionary<Type, int>(variants.Length);
        for (var i = 0; i < variants.Length; i++)
        {
            if (!indexByType.TryAdd(variants[i], i))
                errors.Add(new InvalidOperationException(
                    $"{typeof(TPayload).Name}.Variants lists {variants[i].Name} more than once."));
        }

        // Wire names must be unique so the polymorphic deserializer can dispatch unambiguously.
        var seenWireNames = new Dictionary<string, Type>(declared.Count, StringComparer.Ordinal);
        foreach (var (type, wireName) in declared)
        {
            if (seenWireNames.TryGetValue(wireName, out var priorType))
                errors.Add(new InvalidOperationException(
                    $"{typeof(TPayload).Name}.Variants assigns wire name \"{wireName}\" to both " +
                    $"{priorType.Name} and {type.Name}. Wire names must be unique."));
            else
                seenWireNames[wireName] = type;
        }

        // Every next/previous reference must land on a variant in the list.
        foreach (var variant in variants)
        {
            if (ChainWalk.NextInChain(variant) is { } next && !indexByType.ContainsKey(next))
                errors.Add(new InvalidOperationException(
                    $"{variant.Name}.TUp references {next.Name}, which is not listed in {typeof(TPayload).Name}.Variants."));
            if (ChainWalk.PreviousInChain(variant) is { } prev && !indexByType.ContainsKey(prev))
                errors.Add(new InvalidOperationException(
                    $"{variant.Name}.TDown references {prev.Name}, which is not listed in {typeof(TPayload).Name}.Variants."));
        }

        // Floor = variant with no previous. Ceiling = variant with no next.
        // Must be exactly one of each
        var floors = variants.Where(v => ChainWalk.PreviousInChain(v) is null).ToList();
        var ceilings = variants.Where(v => ChainWalk.NextInChain(v) is null).ToList();

        if (floors.Count == 0)
            errors.Add(new InvalidOperationException(
                $"{typeof(TPayload).Name} declares no floor: every variant has a Downcast target. " +
                "Exactly one variant must implement IFloor or ISole."));
        else if (floors.Count > 1)
            errors.Add(new InvalidOperationException(
                $"{typeof(TPayload).Name} declares {floors.Count} floors ({string.Join(", ", floors.Select(f => f.Name))}). " +
                "Exactly one variant may omit Downcast (i.e., implement IFloor or ISole)."));

        if (ceilings.Count == 0)
            errors.Add(new InvalidOperationException(
                $"{typeof(TPayload).Name} declares no ceiling: every variant has an Upcast target. " +
                "Exactly one variant must implement one of the ICeiling variant kinds (or ISole)."));
        else if (ceilings.Count > 1)
            errors.Add(new InvalidOperationException(
                $"{typeof(TPayload).Name} declares {ceilings.Count} ceilings ({string.Join(", ", ceilings.Select(c => c.Name))}). " +
                "Exactly one variant may omit Upcast."));

        // === Exit 1: Phase 2 relies on no dupes, all refs valid, and exactly one floor/ceiling.
        // Any Phase 1 error violates at least one of those, so bail before the structural walk.
        if (errors.Count > 0) return Wrap(errors);

        // === Phase 2: structural walk ===
        // Sort via NextInChain, then walk the sorted chain to verify each variant's previous
        // points at its predecessor.
        if (TrySort(variants, indexByType, floors[0], errors))
        {
            for (var i = 1; i < variants.Length; i++)
            {
                var current = variants[i];
                var expected = variants[i - 1];
                var actual = ChainWalk.PreviousInChain(current);
                // Phase 1 guarantees exactly one floor (at index 0 after sort), so every
                // non-floor variant declares a Downcast — actual is non-null here.
                if (actual != expected)
                    errors.Add(new InvalidOperationException(
                        $"Chain inconsistency: {current.Name}.TDown is {actual!.Name}, but at position {i} " +
                        $"the chain places {expected.Name} below it."));
            }
        }

        return Wrap(errors);
    }

    /// <summary>
    /// Attempts to sort <paramref name="variants"/> in place from <paramref name="floor"/> at
    /// index 0 to ceiling at index N-1 by following <see cref="ChainWalk.NextInChain"/> at each step.
    /// Appends cycle and premature-termination diagnostics to <paramref name="errors"/>. Returns
    /// <see langword="true"/> only if every variant was placed.
    /// </summary>
    /// <remarks>
    /// Assumes Phase 1 preconditions: no duplicate variants, every Next/Previous reference lands
    /// on a listed variant, and exactly one floor and one ceiling exist.
    /// </remarks>
    private static bool TrySort(
        Type[] variants,
        Dictionary<Type, int> indexByType,
        Type floor,
        List<Exception> errors)
    {
        // The floor goes first
        Swap(variants, indexByType, 0, indexByType[floor]);

        for (var i = 1; i < variants.Length; i++)
        {
            // Fill slot i: the variant already placed at i-1 tells us which type belongs here.
            var previous = variants[i - 1];
            var current = ChainWalk.NextInChain(previous);
            if (current is null)
            {
                // Phase 1 confirmed exactly one ceiling exists; hitting a null Next before
                // position N-1 means it was reached early and remaining variants form an
                // unreachable fork.
                var unreached = variants.Skip(i).Select(v => v.Name).ToArray();
                errors.Add(new InvalidOperationException(
                    $"Chain walk terminates at {previous.Name} (position {i - 1}) but {unreached.Length} variant(s) " +
                    $"remain unreached: {string.Join(", ", unreached)}. Every non-floor variant must be reachable " +
                    "via TUp from the floor."));
                return false;
            }

            // Phase 1 guarantees every Next reference lands in indexByType.
            var foundAt = indexByType[current];

            if (foundAt < i)
            {
                var walked = variants.Take(i).Select(v => v.Name);
                errors.Add(new InvalidOperationException(
                    $"Chain contains a cycle: walked {string.Join(" -> ", walked)} -> {previous.Name}, then " +
                    $"{previous.Name}.TUp is {current.Name}, which is already placed at position {foundAt}."));
                return false;
            }

            // Bring `current` into slot i from wherever it currently sits.
            Swap(variants, indexByType, i, foundAt);
        }

        return true;
    }

    private static void Swap(Type[] variants, Dictionary<Type, int> indexByType, int i, int j)
    {
        if (i == j) return;
        (variants[i], variants[j]) = (variants[j], variants[i]);
        indexByType[variants[i]] = i;
        indexByType[variants[j]] = j;
    }

    private static AggregateException? Wrap(List<Exception> errors) =>
        errors.Count switch
        {
            0 => null,
            _ => new AggregateException(
                $"{typeof(TPayload).Name}'s variant chain has {errors.Count} problem(s). See InnerExceptions.",
                errors),
        };
}
