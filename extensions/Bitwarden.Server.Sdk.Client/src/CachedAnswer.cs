using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What the service-owned cache says about one read.
/// </summary>
public readonly record struct CachedAnswer
{
    private CachedAnswer(CachedAnswerKind kind, string? resource)
    {
        Kind = kind;
        Resource = resource;
    }

    /// <summary>
    /// Whether it answered, and how.
    /// </summary>
    public CachedAnswerKind Kind { get; }

    /// <summary>
    /// On a hit, the resource object as the service would have sent it in <c>data</c>, with only
    /// the attributes asked for.
    /// </summary>
    public string? Resource { get; }

    /// <summary>
    /// The cache cannot answer: ask the service.
    /// </summary>
    public static CachedAnswer Miss { get; } = new(CachedAnswerKind.Miss, null);

    /// <summary>
    /// The service has nothing under the key, and said so.
    /// </summary>
    public static CachedAnswer NothingRecorded { get; } = new(CachedAnswerKind.NothingRecorded, null);

    internal static CachedAnswer Hit(string resource) => new(CachedAnswerKind.Hit, resource);

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
}
