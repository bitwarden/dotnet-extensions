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
}
