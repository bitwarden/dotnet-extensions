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
        _logger.LogError("{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));

    public void LogError(Exception ex, string format, params object[] args) =>
        _logger.LogError(ex, "{Message}", string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
}
