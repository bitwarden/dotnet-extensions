namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// Where in the request document a binding or validation failure is.
/// </summary>
internal static class RequestPointer
{
    /// <summary>
    /// Turns a validation key into a JSON Pointer into the request document.
    /// </summary>
    /// <remarks>
    /// A key names the member path as the binder saw it (<c>Data.Email</c>), while the standards
    /// want a pointer into the wire document, which carries the attributes envelope:
    /// <c>/data/attributes/email</c>. An empty key refers to the document as a whole.
    /// </remarks>
    internal static string For(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "/data";
        }

        // A body error is keyed as a JSON path ("$.data.email") whose root segment is not part
        // of a pointer. A collection's items are indexed ("$.data[1].lid", or "Data[1].Name"
        // from validation), and an index is a segment of its own in a pointer.
        var segments = Segments(key)
            .Where(segment => segment.Length > 0 && segment != "$")
            .ToArray();

        if (segments.Length == 0)
        {
            return "/data";
        }

        // One of a collection's resources: the index leads, and below it the same rule as for
        // a single resource.
        if (segments is ["data", var index, ..] && int.TryParse(index, out _))
        {
            return segments.Length > 2 && segments[2] is not ("id" or "type" or "lid")
                ? $"/data/{index}/attributes/" + string.Join('/', segments[2..])
                : '/' + string.Join('/', segments);
        }

        // Anything below the resource itself lives under attributes on the wire.
        return segments is ["data", var member, ..] && member is not ("id" or "type")
            ? "/data/attributes/" + string.Join('/', segments[1..])
            : '/' + string.Join('/', segments);
    }

    /// <summary>
    /// A key's segments as they are named on the wire.
    /// </summary>
    /// <remarks>
    /// A member's name is camel-cased. What is in brackets is an index or a dictionary's key
    /// (<c>Items[Home].Number</c>), sent as it is, so it is kept as it is, escaped as a pointer
    /// escapes a segment.
    /// </remarks>
    private static IEnumerable<string> Segments(string key)
    {
        var at = 0;

        while (at < key.Length)
        {
            if (key[at] == '[')
            {
                var close = key.IndexOf(']', at + 1);
                var end = close < 0 ? key.Length : close;

                yield return key[(at + 1)..end].Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

                at = end + 1;
            }
            else if (key[at] == '.')
            {
                at++;
            }
            else
            {
                var end = key.IndexOfAny(['.', '['], at);
                end = end < 0 ? key.Length : end;

                yield return char.ToLowerInvariant(key[at]) + key[(at + 1)..end];

                at = end;
            }
        }
    }
}
