using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What the service-owned cache says about one read.
/// </summary>
public readonly record struct CachedAnswer
{
    private CachedAnswer(CachedAnswerKind kind, string? resource, int? total = null)
    {
        Kind = kind;
        Resource = resource;
        Total = total;
    }

    /// <summary>
    /// Whether it answered, and how.
    /// </summary>
    public CachedAnswerKind Kind { get; }

    /// <summary>
    /// On a hit, the resource object as the service would have sent it in <c>data</c>, with only
    /// the attributes asked for; for a page of a cached read-many, the array of them.
    /// </summary>
    public string? Resource { get; }

    /// <summary>
    /// On a hit of a cached read-many, how many resources the party has in all, as the service's <c>totalCount</c>
    /// says; null otherwise.
    /// </summary>
    public int? Total { get; }

    /// <summary>
    /// The cache cannot answer: ask the service.
    /// </summary>
    public static CachedAnswer Miss { get; } = new(CachedAnswerKind.Miss, null);

    /// <summary>
    /// The service has nothing under the key, and said so.
    /// </summary>
    public static CachedAnswer NothingRecorded { get; } = new(CachedAnswerKind.NothingRecorded, null);

    internal static CachedAnswer Hit(string resource) => new(CachedAnswerKind.Hit, resource);

    internal static CachedAnswer HitPage(string resources, int total) => new(CachedAnswerKind.Hit, resources, total);

    /// <summary>
    /// The hit as the generated resource type, or null for anything else.
    /// </summary>
    /// <typeparam name="T">The generated resource type, as NSwag generates it with Newtonsoft.</typeparam>
    /// <returns>The resource, or null.</returns>
    /// <remarks>
    /// Read leniently: an attribute the contract marks required may be absent, because a cached answer
    /// holds only the attributes asked for, and the caller reads only those. The service's own answer
    /// carries them (<see cref="Fieldsets.ForService{TAttributes}"/>), so its reading is unchanged.
    /// </remarks>
    [RequiresUnreferencedCode("Newtonsoft reads the generated type by reflection.")]
    [RequiresDynamicCode("Newtonsoft reads the generated type by reflection.")]
    public T? As<T>()
        where T : class =>
        Kind == CachedAnswerKind.Hit ? CachedResources.Read<T>(Resource!) : null;

    /// <summary>
    /// A cached read-many's page as the generated resource type, in its order, or null for anything else.
    /// </summary>
    /// <typeparam name="T">The generated resource type.</typeparam>
    /// <returns>The resources, or null.</returns>
    /// <remarks>
    /// Read leniently, as <see cref="As{T}"/> reads one.
    /// </remarks>
    [RequiresUnreferencedCode("Newtonsoft reads the generated type by reflection.")]
    [RequiresDynamicCode("Newtonsoft reads the generated type by reflection.")]
    public IReadOnlyList<T>? AsList<T>()
        where T : class =>
        Kind == CachedAnswerKind.Hit ? CachedResources.Read<List<T>>(Resource!) : null;
}
