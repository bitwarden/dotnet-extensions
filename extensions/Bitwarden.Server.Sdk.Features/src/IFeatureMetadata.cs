namespace Bitwarden.Server.Sdk.Features;

/// <summary>
/// A piece of metadata that can be added to an endpoint to gate usage of that endpoint.
/// </summary>
public interface IFeatureMetadata
{
    /// <summary>
    /// A method to run to check if the feature is enabled. Should return <see langword="true" /> if the feature
    /// can be used, returns <see langword="false" /> if not.
    /// </summary>
    Func<IFeatureService, bool> FeatureCheck { get; set; }

    /// <summary>
    /// Creats a display string that can be used in logs and development error messages for better debugging.
    /// </summary>
    /// <returns></returns>
    string ToString();
}
