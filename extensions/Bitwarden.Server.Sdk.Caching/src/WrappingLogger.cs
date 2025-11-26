using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.Caching;

internal class WrappingLogger : ILogger<FusionCache>
{
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private readonly ILogger _logger;

    public WrappingLogger(ILoggerFactory loggerFactory, string name)
    {
        _logger = loggerFactory.CreateLogger($"ZiggyCreatures.Caching.Fusion.FusionCache.{name}");
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _logger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel)
        => _logger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _logger.Log(logLevel, eventId, state, exception, formatter);
}
