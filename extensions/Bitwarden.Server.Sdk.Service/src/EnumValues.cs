using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// How an enumeration's members appear on the wire.
/// </summary>
/// <remarks>
/// The standards require an enumerated value to be upper snake case (<c>ALL_CAPS</c>), so the
/// naming policy and the validation that rejects unrecognised values have to agree on what a
/// member is called. Both read it from here, because a validator that accepts what the
/// serializer will not write is worse than no validator at all.
/// </remarks>
public static class EnumValues
{
    /// <summary>
    /// The policy every enumerated value is published under.
    /// </summary>
    public static readonly JsonNamingPolicy Naming = JsonNamingPolicy.SnakeCaseUpper;

    /// <summary>
    /// The published values of an enumeration, in declaration order.
    /// </summary>
    /// <param name="type">The enumeration.</param>
    public static string[] Of(Type type) => [.. Enum.GetNames(type).Select(Naming.ConvertName)];

    /// <summary>
    /// Whether a value on the wire names a member of an enumeration.
    /// </summary>
    /// <param name="type">The enumeration.</param>
    /// <param name="value">The value as the caller sent it.</param>
    public static bool Recognises(Type type, string value) =>
        Of(type).Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a value on the wire back into the member it names.
    /// </summary>
    /// <param name="type">The enumeration.</param>
    /// <param name="value">The value as the caller sent it.</param>
    /// <param name="member">The member, when the value names one.</param>
    /// <returns>Whether the value names a member.</returns>
    /// <remarks>
    /// Matched against the published names, never parsed from the text. Turning <c>USER_LOGGED_IN</c>
    /// back into a CLR name by stripping underscores works for <c>UserLoggedIn</c> and fails for
    /// <c>User_LoggedIn</c>, which publishes the same way; going through the pairing of each member
    /// with its published name cannot disagree with how it was written.
    /// </remarks>
    public static bool TryParse(Type type, string value, out object? member)
    {
        var names = Enum.GetNames(type);

        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(Naming.ConvertName(names[i]), value, StringComparison.OrdinalIgnoreCase))
            {
                member = Enum.Parse(type, names[i]);
                return true;
            }
        }

        member = null;
        return false;
    }
}
