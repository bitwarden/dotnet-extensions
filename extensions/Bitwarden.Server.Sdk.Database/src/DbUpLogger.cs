using DbUp.Engine.Output;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Database;

internal sealed class DbUpLogger : IUpgradeLog
{
    private readonly ILogger _logger;

    public DbUpLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void LogTrace(string format, params object[] args) =>
        _logger.LogTrace("{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));

    public void LogDebug(string format, params object[] args) =>
        _logger.LogDebug("{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));

    public void LogInformation(string format, params object[] args) =>
        _logger.LogInformation("{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));

    public void LogWarning(string format, params object[] args) =>
        _logger.LogWarning("{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));

    public void LogError(string format, params object[] args) =>
        _logger.LogError("{Message}", Redacted(format, args));

    // The exception isn't attached: its message is the database's, which the migration still throws.
    public void LogError(Exception ex, string format, params object[] args) =>
        _logger.LogError("{Message}", Redacted(format, args));

    /// <summary>
    /// DbUp's error lines repeat the database's message, which can carry row values such as a
    /// duplicate key. Numbers (script block, line, SQL error number) are kept; text is not, and a
    /// line that is itself an exception dump is dropped whole.
    /// </summary>
    private static string Redacted(string format, object[] args)
    {
        if (args.Length == 0)
            return format.Contains("Exception", StringComparison.Ordinal) ? "[redacted]" : format;

        var safe = args.Select(arg => arg is byte or short or int or long ? arg : "[redacted]").ToArray();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, format, safe);
    }
}
