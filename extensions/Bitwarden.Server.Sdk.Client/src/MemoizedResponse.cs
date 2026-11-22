using System.Net;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A service's answer as the request memo holds it: its status, headers and body bytes, from which each caller is given
/// a response of its own. Or, for an answer too large to hold, the one response itself, for the caller that read it.
/// </summary>
internal sealed class MemoizedResponse
{
    private readonly HttpResponseMessage? _original;
    private readonly HttpStatusCode _status;
    private readonly Version _version = HttpVersion.Version11;
    private readonly string? _reason;
    private readonly KeyValuePair<string, string[]>[] _headers = [];
    private readonly KeyValuePair<string, string[]>[] _contentHeaders = [];
    private readonly byte[] _body = [];
    private readonly long? _headContentLength;

    private MemoizedResponse(HttpResponseMessage original)
    {
        _original = original;
    }

    private MemoizedResponse(HttpResponseMessage response, byte[] body, bool head)
    {
        _status = response.StatusCode;
        _version = response.Version;
        _reason = response.ReasonPhrase;
        _headers = [.. response.Headers.Select(header => new KeyValuePair<string, string[]>(header.Key, [.. header.Value]))];
        _contentHeaders =
        [
            .. response.Content.Headers
                .Where(header => !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                .Select(header => new KeyValuePair<string, string[]>(header.Key, [.. header.Value])),
        ];
        _body = body;
        _headContentLength = head ? response.Content.Headers.ContentLength : null;
    }

    /// <summary>
    /// Whether it can be given to more than the caller that read it.
    /// </summary>
    public bool Shareable => _original is null;

    /// <summary>
    /// What it holds, counted against the memo's bound: the body and, roughly, the headers.
    /// </summary>
    public long Size =>
        _body.Length
        + _headers.Sum(header => header.Key.Length + header.Value.Sum(value => value.Length))
        + _contentHeaders.Sum(header => header.Key.Length + header.Value.Sum(value => value.Length));

    /// <summary>
    /// Takes the answer out of <paramref name="response"/>: its body read into memory, up to <paramref name="maxBytes"/>,
    /// and the response disposed. A body larger than that is left in the response, which is passed on alone.
    /// </summary>
    public static async Task<MemoizedResponse> TakeAsync(
        HttpResponseMessage response,
        bool head,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (!head && response.Content.Headers.ContentLength is { } length && length > maxBytes)
        {
            return new MemoizedResponse(response);
        }

        byte[] read;
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, count);

                if (buffer.Length > maxBytes)
                {
                    // Too large to hold: the caller reads what was read and then the rest, as if nothing had read it.
                    var rest = new PrefixedStream(buffer.ToArray(), stream);
                    var content = new StreamContent(rest);
                    foreach (var header in response.Content.Headers)
                    {
                        content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    response.Content = content;
                    return new MemoizedResponse(response);
                }
            }

            read = buffer.ToArray();
        }
        catch
        {
            // A body that failed part way is the caller's failure, and the response is nobody's.
            response.Dispose();
            throw;
        }

        var answer = new MemoizedResponse(response, read, head);
        response.Dispose();

        return answer;
    }

    /// <summary>
    /// A response for <paramref name="request"/>: a new one over its own copy of the body, or the one response itself
    /// for the caller that read an answer too large to hold.
    /// </summary>
    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (_original is not null)
        {
            return _original;
        }

        var response = new HttpResponseMessage(_status)
        {
            Version = _version,
            ReasonPhrase = _reason,
            RequestMessage = request,
            Content = new ByteArrayContent(_body),
        };

        foreach (var header in _headers)
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in _contentHeaders)
        {
            response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (_headContentLength is { } length)
        {
            response.Content.Headers.ContentLength = length;
        }

        return response;
    }
}
