namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// The access shape a use of a restricted dependency takes. Each kind has its own diagnostic id
/// and its own row shape in the baseline.
/// </summary>
public enum DependencyUsageType
{
    /// <summary>
    /// A constructor parameter of the restricted type (BW0005).
    /// </summary>
    Injection,

    /// <summary>
    /// A call to, or reference of, a restricted member (BW0006).
    /// </summary>
    Member,

    /// <summary>
    /// Resolution from IServiceProvider, RequestServices or ActivatorUtilities (BW0007).
    /// </summary>
    Locator,

    /// <summary>
    /// A reference to a type that implements the restricted type (BW0008).
    /// </summary>
    Concrete,

    /// <summary>
    /// The restricted type leaving a class through a signature, non-private storage, generic
    /// argument or base-constructor argument (BW0009).
    /// </summary>
    Escape,
}
