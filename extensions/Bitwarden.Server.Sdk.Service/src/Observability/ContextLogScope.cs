using System.Diagnostics;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Observability;

/// <summary>
/// Puts what a request is about onto every log message and every trace it produces.
/// </summary>
/// <remarks>
/// <para>
/// A service that takes its scope from headers rather than from the path pays for it in
/// observability: <c>GET /api/v1/memberships</c> reads identically for ten thousand
/// organizations, so a log line, a trace or an error report says what happened without saying
/// who it happened to. This is the compensating mechanism, and it is middleware rather than a
/// convention so that no author has to remember it.
/// </para>
/// <para>
/// Only what is present. A request about a personal vault names no organization, and a field
/// written as "none" would be indistinguishable from one the caller forgot, which is the thing
/// somebody reading the log is usually trying to find out.
/// </para>
/// <para>
/// A scope for logs and tags for the trace, rather than baggage. Baggage travels to every
/// service downstream, where the same values already arrive as headers; writing it would
/// duplicate them on the wire and make the context look like something propagated by the
/// tracer rather than asserted by the caller.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="logger">Holds the scope for the duration of the request.</param>
internal sealed class ContextLogScope(RequestDelegate next, ILogger<ContextLogScope> logger)
{
    /// <summary>
    /// The organization a request is about.
    /// </summary>
    private const string Organization = "OrganizationId";

    /// <summary>
    /// The user a request is made on behalf of.
    /// </summary>
    private const string User = "UserId";

    /// <summary>
    /// The provider a request is about.
    /// </summary>
    private const string Provider = "ProviderId";

    public async Task InvokeAsync(HttpContext http, IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The context comes from the validated token, which this runs before, so
        // the scope reads it as each message is written rather than once, here. A message logged
        // before authentication has run carries nothing, which is what is known then.
        var activity = Activity.Current;

        try
        {
            using var scope = logger.BeginScope(new ContextFields(() => Fields(context)));

            await next(http);
        }
        finally
        {
            // The same facts on the span, so a trace is attributable without joining it to the
            // logs; once the request has been handled, when they are known. Dotted and
            // lower-cased, which is the convention every tracing backend expects of an attribute
            // name.
            if (activity is not null)
            {
                foreach (var field in Fields(context))
                {
                    activity.SetTag($"bitwarden.{Naming(field.Key)}", field.Value);
                }
            }
        }
    }

    /// <summary>
    /// The fields present now.
    /// </summary>
    private static List<KeyValuePair<string, object>> Fields(IContext context)
    {
        var fields = new List<KeyValuePair<string, object>>(3);

        Note(fields, Organization, context.OrganizationId);
        Note(fields, User, context.UserId);
        Note(fields, Provider, context.ProviderId);

        return fields;
    }

    /// <remarks>
    /// The identifier as its own field rather than folded into the message, so a log search can
    /// ask for one organization's requests rather than grep for a string that happens to look
    /// like a GUID.
    /// </remarks>
    private static void Note(List<KeyValuePair<string, object>> fields, string name, Guid? value)
    {
        if (value is not { } present)
        {
            return;
        }

        fields.Add(new KeyValuePair<string, object>(name, present));
    }

    private static string Naming(string field) => field switch
    {
        Organization => "organization_id",
        User => "user_id",
        _ => "provider_id",
    };
}
