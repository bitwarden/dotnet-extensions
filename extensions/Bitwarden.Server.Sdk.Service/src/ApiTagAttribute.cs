namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Names and explains the section an endpoints class's operations appear under.
/// </summary>
/// <remarks>
/// <para>
/// Left alone, the section is the endpoints class's type name without its <c>Endpoints</c> suffix
/// (<c>ApiKeys</c>, <c>ProviderUsers</c>),
/// which is a C# identifier showing through into a published contract. The platform splits
/// that into words by default, so <c>ProviderUsers</c> reads as "Provider Users" with nothing
/// declared. Give a name where the split is wrong: an acronym the splitter cannot know about,
/// most often.
/// </para>
/// <para>
/// The description is the part worth writing. OpenAPI carries one per tag, and a
/// documentation UI shows it under the section heading, so it is the one place to say what a
/// resource <em>is</em> before a reader starts working through its operations. Nothing else in
/// the document has room for that: an operation summary explains one call, not the concept.
/// </para>
/// </remarks>
/// <param name="name">
/// The section's name. Defaults to the endpoints class's name, without its <c>Endpoints</c> suffix,
/// split into words.
/// </param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiTagAttribute(string? name = null) : Attribute
{
    /// <summary>
    /// The section's name, or null to split the endpoints class's name into words.
    /// </summary>
    public string? Name { get; } = name;

    /// <summary>
    /// What the resources in this section are, in a sentence or two.
    /// </summary>
    /// <remarks>
    /// Written for somebody who has not met the concept. "Credentials issued to the
    /// organization rather than to a person" tells a reader something; "API key operations"
    /// tells them what they already knew from the heading.
    /// </remarks>
    public string? Description { get; init; }
}
