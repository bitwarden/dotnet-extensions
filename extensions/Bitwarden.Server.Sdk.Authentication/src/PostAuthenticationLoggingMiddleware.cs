using System.Collections;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Authentication;

internal sealed class PostAuthenticationLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PostAuthenticationLoggingMiddleware> _logger;

    public PostAuthenticationLoggingMiddleware(RequestDelegate next, ILogger<PostAuthenticationLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public Task Invoke(HttpContext context)
    {
        if (context.User.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            return _next(context);
        }

        var subject = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrEmpty(subject))
        {
            return _next(context);
        }

        using (_logger.BeginScope(new AuthenticatedUserLogScope(subject)))
        {
            return _next(context);
        }
    }

    private sealed class AuthenticatedUserLogScope : IReadOnlyList<KeyValuePair<string, object>>
    {
        public string Subject { get; }

        int IReadOnlyCollection<KeyValuePair<string, object>>.Count { get; } = 1;

        KeyValuePair<string, object> IReadOnlyList<KeyValuePair<string, object>>.this[int index]
        {
            get
            {
                if (index == 0)
                {
                    return new KeyValuePair<string, object>(nameof(Subject), Subject);
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public AuthenticatedUserLogScope(string subject)
        {
            Subject = subject;
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            yield return new KeyValuePair<string, object>(nameof(Subject), Subject);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<string, object>>)this).GetEnumerator();
        }
    }
}
