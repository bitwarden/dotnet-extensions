using System.Diagnostics;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Tests.Observability;

/// <summary>
/// What a request is about, on every message it logs.
/// </summary>
/// <remarks>
/// The compensating mechanism for taking scope from headers rather than from the path:
/// <c>GET /api/v1/memberships</c> reads identically for every organization, so without this a
/// log line says what happened and not who it happened to.
/// </remarks>
public sealed class ContextLogScopeTests
{
    [Fact]
    public async Task Invoke_WithAnOrganizationAndUser_PutsBothOnTheMessage()
    {
        var organization = Guid.NewGuid();
        var user = Guid.NewGuid();

        var logged = await Logging(new StatedContext
        {
            OrganizationId = organization,
            UserId = user,
        });

        Assert.Equal(organization, logged["OrganizationId"]);
        Assert.Equal(user, logged["UserId"]);
    }

    // A request about a personal vault names no organization. A field written as "none" would be
    // indistinguishable from one the caller forgot, which is usually the thing being looked for.
    [Fact]
    public async Task Invoke_WithNoOrganization_OmitsTheField()
    {
        var logged = await Logging(new StatedContext { UserId = Guid.NewGuid() });

        Assert.DoesNotContain("OrganizationId", logged.Keys);
        Assert.Contains("UserId", logged.Keys);
    }

    [Fact]
    public async Task Invoke_WithAProvider_PutsItOnTheMessage()
    {
        var provider = Guid.NewGuid();

        var logged = await Logging(new StatedContext { ProviderId = provider });

        Assert.Equal(provider, logged["ProviderId"]);
    }

    // No scope at all rather than an empty one: a scope with no fields still allocates and
    // still appears in every message's scope list.
    [Fact]
    public async Task Invoke_WithNothingInContext_OpensNoScope()
    {
        var logged = await Logging(new StatedContext());

        Assert.Empty(logged);
    }

    // So a trace is attributable without being joined to the logs.
    [Fact]
    public async Task Invoke_TagsTheSpan()
    {
        var organization = Guid.NewGuid();

        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };

        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("test");
        using var activity = source.StartActivity("request");

        await Logging(new StatedContext { OrganizationId = organization });

        Assert.Equal(
            organization.ToString(),
            activity?.GetTagItem("bitwarden.organization_id")?.ToString());
    }

    /// <summary>
    /// Runs the middleware and returns the fields the scope carried.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, object>> Logging(IContext context)
    {
        var logger = new ScopeRecordingLogger();
        var middleware = new ContextLogScope(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(new DefaultHttpContext(), context);

        return logger.Fields;
    }

    /// <remarks>
    /// Records what a scope was opened with. The platform's own loggers write scopes to an
    /// output this cannot read, so what is asserted is what the middleware asked for.
    /// </remarks>
    private sealed class ScopeRecordingLogger : ILogger<ContextLogScope>
    {
        public Dictionary<string, object> Fields { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var pair in pairs)
                {
                    Fields[pair.Key] = pair.Value;
                }
            }

            return new Nothing();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
