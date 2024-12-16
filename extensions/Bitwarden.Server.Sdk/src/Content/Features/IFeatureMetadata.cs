namespace Bitwarden.Server.Sdk.Features;

internal interface IFeatureMetadata
{
    /// <summary>
    /// A method to run to check if the feature is enabled.
    /// </summary>
    Func<IFeatureService, bool> FeatureCheck { get; set; }
}
