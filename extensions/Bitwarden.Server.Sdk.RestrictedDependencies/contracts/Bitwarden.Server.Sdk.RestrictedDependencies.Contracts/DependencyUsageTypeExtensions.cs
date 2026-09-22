namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// Converts a <see cref="DependencyUsageType"/> to and from the lower-case token the baseline
/// stores.
/// </summary>
public static class DependencyUsageTypeExtensions
{
    /// <summary>
    /// The lower-case token written to the baseline's <c>kind</c> field.
    /// </summary>
    /// <param name="kind">The access shape to render.</param>
    /// <returns>The baseline token for <paramref name="kind"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the five shapes.</exception>
    public static string ToToken(this DependencyUsageType kind) => kind switch
    {
        DependencyUsageType.Injection => "injection",
        DependencyUsageType.Member => "member",
        DependencyUsageType.Locator => "locator",
        DependencyUsageType.Concrete => "concrete",
        DependencyUsageType.Escape => "escape",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Parses a baseline <c>kind</c> token; returns false for anything unknown.
    /// </summary>
    /// <param name="token">The token read from the baseline.</param>
    /// <param name="kind">The parsed access shape, or <c>default</c> when the token is unknown.</param>
    /// <returns>True when <paramref name="token"/> named one of the five shapes.</returns>
    public static bool TryParse(string token, out DependencyUsageType kind)
    {
        switch (token)
        {
            case "injection": kind = DependencyUsageType.Injection; return true;
            case "member": kind = DependencyUsageType.Member; return true;
            case "locator": kind = DependencyUsageType.Locator; return true;
            case "concrete": kind = DependencyUsageType.Concrete; return true;
            case "escape": kind = DependencyUsageType.Escape; return true;
            default: kind = default; return false;
        }
    }
}
