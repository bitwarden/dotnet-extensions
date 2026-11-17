namespace Bitwarden.Server.Sdk.Service.Observability;

/// <summary>
/// The fields a request's log scope carries.
/// </summary>
/// <remarks>
/// A type rather than a dictionary, for one reason: a structured provider reads the pairs, and
/// the plain console formatter calls <see cref="ToString"/> on whatever the scope holds. A
/// dictionary handed to the second prints
/// <c>System.Collections.Generic.Dictionary`2[System.String,System.Object]</c>.
/// </remarks>
/// <param name="read">Reads the pairs present now, in the order they are noted.</param>
internal sealed class ContextFields(Func<IReadOnlyList<KeyValuePair<string, object>>> read)
    : IReadOnlyList<KeyValuePair<string, object>>
{
    public KeyValuePair<string, object> this[int index] => read()[index];

    public int Count => read().Count;

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => read().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    public override string ToString() =>
        string.Join(' ', read().Select(field => $"{field.Key}:{field.Value}"));
}
