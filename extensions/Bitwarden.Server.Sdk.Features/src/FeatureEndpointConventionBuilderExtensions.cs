using Bitwarden.Server.Sdk.Features;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Feature extension methods for <see cref="IEndpointConventionBuilder"/>.
/// </summary>
public static class FeatureEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Adds a feature check for the given feature to the endpoint(s).
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="featureNameKey"></param>
    /// <returns></returns>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string featureNameKey)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(featureNameKey);

        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new RequireFeatureAttribute(featureNameKey));
        });

        return builder;
    }

    /// <summary>
    /// Adds a feature check with the specified check to the endpoint(s).
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="featureCheck"></param>
    /// <returns></returns>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, Func<IFeatureService, bool> featureCheck)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(featureCheck);

        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new RequireFeatureAttribute(featureCheck));
        });

        return builder;
    }
}
