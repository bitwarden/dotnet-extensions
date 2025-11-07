using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Features;

internal static partial class LoggerExtensions
{
    [LoggerMessage(1, LogLevel.Warning, "No endpoint set -- did you forget to call 'UseRouting()'?")]
    public static partial void LogNoEndpointWarning(this ILogger logger);

    [LoggerMessage(2, LogLevel.Debug, "Failed feature check {CheckName}", SkipEnabledCheck = true)]
    public static partial void LogFailedFeatureCheck(this ILogger logger, string checkName);

    [LoggerMessage(3, LogLevel.Warning, "No HttpContext available for the current feature flag check.")]
    public static partial void LogMissingHttpContext(this ILogger logger);

    [LoggerMessage(4, LogLevel.Warning, "Assembly {Assembly} missing informational version attribute")]
    public static partial void LogMissingVersionAttribute(this ILogger logger, string assembly);

    [LoggerMessage(5, LogLevel.Warning, "The given version {Version} could not be parsed.")]
    public static partial void LogInvalidVersion(this ILogger logger, string version);

    [LoggerMessage(6, LogLevel.Warning, "No assembly could be loaded with the name {ApplicationName}")]
    public static partial void LogNoAssemblyFound(this ILogger logger, string applicationName);

    [LoggerMessage(7, LogLevel.Warning, "LaunchDarkly flags state is invalid, returning empty flags")]
    public static partial void LogInvalidFlagsState(this ILogger logger);
}
