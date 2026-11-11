using System.Globalization;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// What the parity handlers answer with.
/// </summary>
internal static class ParityAnswers
{
    public static ParityItem Item(string id, string note) => new() { Id = id, Name = "item", Quantity = 1, Note = note };

    public static Paged<ParityItem> Three(string note) =>
        new([Item("one", note), Item("two", note), Item("three", note)], 3);

    /// <summary>
    /// Formats a note in the invariant culture, down to the values a record's <c>ToString</c>
    /// formats, so the recorded answers hold on every machine.
    /// </summary>
    public static string Invariant(Func<string> note)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            return note();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
