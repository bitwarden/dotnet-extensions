using Microsoft.Extensions.Logging;

namespace Bitwarden.Extensions.Hosting.Features;

internal static partial class LoggerExtensions
{
    [LoggerMessage(1, LogLevel.Warning, "No Endpoint Set, Maybe you forgot to call 'UseRouting()'.")]
    public static partial void LogNoEndpointWarning(this ILogger logger);

    [LoggerMessage(2, LogLevel.Debug, "Failed feature check {CheckName}", SkipEnabledCheck = true)]
    public static partial void LogFailedFeatureCheck(this ILogger logger, string checkName);
}
