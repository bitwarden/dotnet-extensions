namespace Bitwarden.Server.Sdk.Features;

/// <summary>
/// Marks a class as a collection of feature flag keys.
/// The source generator will use this attribute to generate the necessary code
/// to integrate the flag keys with the feature flag system.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FlagKeyCollectionAttribute : Attribute
{

}
