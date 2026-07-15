using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Environment;

internal static partial class LoggerExtensions
{
    [LoggerMessage(1, LogLevel.Warning, "Assembly {Assembly} missing informational version attribute")]
    public static partial void LogMissingVersionAttribute(this ILogger logger, string assembly);

    [LoggerMessage(2, LogLevel.Warning, "The given version {Version} could not be parsed.")]
    public static partial void LogInvalidVersion(this ILogger logger, string version);

    [LoggerMessage(3, LogLevel.Warning, "No assembly could be loaded with the name {ApplicationName}")]
    public static partial void LogNoAssemblyFound(this ILogger logger, string applicationName);
}
