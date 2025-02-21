namespace Bitwarden.Server.Sdk.Features;

/// <summary>
/// Specifies that the class or method that this attribute is applied to requires a feature check to run.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireFeatureAttribute : Attribute, IFeatureMetadata
{
    private readonly string _stringRepresentation;

    /// <summary>
    /// Initializes a new instance of <see cref="RequireFeatureAttribute"/>
    /// </summary>
    /// <param name="featureFlagKey">The feature flag that should be enabled.</param>
    public RequireFeatureAttribute(string featureFlagKey)
    {
        ArgumentNullException.ThrowIfNull(featureFlagKey);

        _stringRepresentation = $"Flag = {featureFlagKey}";
        FeatureCheck = (featureService) => featureService.IsEnabled(featureFlagKey);
    }

    internal RequireFeatureAttribute(Func<IFeatureService, bool> featureCheck)
    {
        ArgumentNullException.ThrowIfNull(featureCheck);

        FeatureCheck = featureCheck;
        _stringRepresentation = "Custom Feature Check";
    }

    /// <inheritdoc />
    public Func<IFeatureService, bool> FeatureCheck { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return _stringRepresentation;
    }
}
