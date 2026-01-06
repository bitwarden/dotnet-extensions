using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Caching;

// A class that exists to satisfy the `ILogger<T>` interface but we don't want the default name that
// comes from T and instead want to use our own category name
internal class NamedLogger<T> : ILogger<T>
{
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private readonly ILogger _logger;

    public NamedLogger(ILoggerFactory loggerFactory, string name)
    {
        _logger = loggerFactory.CreateLogger(name);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _logger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel)
        => _logger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => _logger.Log(logLevel, eventId, state, exception, formatter);
}
