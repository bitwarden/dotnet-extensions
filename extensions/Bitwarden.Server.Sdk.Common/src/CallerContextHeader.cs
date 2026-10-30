using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Writes and reads <see cref="BitwardenHeaders.Context"/>.
/// </summary>
/// <remarks>
/// Reading is strict. An unknown member, a member twice, a value of the wrong kind or shape, or a
/// header longer than <see cref="MaximumLength"/> bytes is refused rather than partly believed. Only
/// the platform writes this header, so a malformed one is a defect or a forgery.
/// </remarks>
public static partial class CallerContextHeader
{
    /// <summary>
    /// The version this writes and the only one it reads.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// The longest value accepted, in bytes.
    /// </summary>
    public const int MaximumLength = 16384;

    /// <summary>
    /// The most organizations <see cref="CallerContextValue.Organizations"/> may list. A person in more
    /// is carried without the member.
    /// </summary>
    public const int MaximumOrganizations = 300;

    private const string VersionMember = "v";
    private const string ClientIpMember = "clientIp";
    private const string DeviceTypeMember = "deviceType";
    private const string DeviceIdentifierMember = "deviceIdentifier";
    private const string CredentialMember = "credential";
    private const string RecordedUserMember = "recordedUser";
    private const string RecordedProviderMember = "recordedProvider";
    private const string OrganizationsMember = "organizations";

    /// <summary>
    /// Writes a value.
    /// </summary>
    /// <param name="value">What to carry.</param>
    /// <returns>
    /// Compact JSON with <c>v</c> first and absent members omitted. With nothing else to carry it is
    /// <c>{"v":1}</c>.
    /// </returns>
    /// <exception cref="ArgumentException">A member is one <see cref="TryParse"/> would refuse.</exception>
    public static string Write(CallerContextValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var problem = Check(value);
        if (problem is not null)
        {
            throw new ArgumentException($"The caller context can't be sent: {problem}", nameof(value));
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionMember, Version);
            WriteIfPresent(writer, ClientIpMember, value.ClientIp);
            WriteIfPresent(writer, DeviceTypeMember, value.DeviceType);
            WriteIfPresent(writer, DeviceIdentifierMember, value.DeviceIdentifier);
            WriteIfPresent(writer, CredentialMember, value.Credential is { } credential ? Name(credential) : null);
            WriteIfPresent(writer, RecordedUserMember, value.RecordedUser?.ToString());
            WriteIfPresent(writer, RecordedProviderMember, value.RecordedProvider?.ToString());
            if (value.Organizations is { Count: > 0 } organizations)
            {
                writer.WriteStartArray(OrganizationsMember);
                foreach (var organization in organizations)
                {
                    writer.WriteStringValue(organization.ToString());
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a value strictly.
    /// </summary>
    /// <param name="header">The header's value.</param>
    /// <param name="value">What it carries, when it is well formed.</param>
    /// <param name="problem">Why it was refused, otherwise: a sentence naming the member, never echoing a value.</param>
    /// <returns>Whether it is well formed.</returns>
    public static bool TryParse(string? header, out CallerContextValue value, out string? problem)
    {
        value = new CallerContextValue();

        if (string.IsNullOrEmpty(header))
        {
            problem = "It is empty.";
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(header);
        if (bytes.Length > MaximumLength)
        {
            problem = $"It is longer than {MaximumLength} bytes.";
            return false;
        }

        try
        {
            var members = new Members();
            problem = ReadObject(bytes, members) ?? ToValue(members, out value);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            problem = "It is not well-formed JSON.";
        }

        return problem is null;
    }

    /// <summary>
    /// Whether a value is a device type: up to 50 letters, digits, hyphens and underscores.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsDeviceType(string? value) => value is not null && DeviceTypePattern().IsMatch(value);

    /// <summary>
    /// Whether a value is a device identifier: up to 50 letters, digits, <c>-</c>, <c>_</c> and <c>:</c>,
    /// which is what a push tag allows.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsDeviceIdentifier(string? value) => value is not null && DeviceIdentifierPattern().IsMatch(value);

    /// <summary>
    /// The wire name of a credential.
    /// </summary>
    /// <param name="credential">The credential.</param>
    public static string Name(CallerCredential credential) => credential switch
    {
        CallerCredential.User => "USER",
        CallerCredential.Organization => "ORGANIZATION",
        CallerCredential.Machine => "MACHINE",
        CallerCredential.System => "SYSTEM",
        _ => throw new ArgumentOutOfRangeException(nameof(credential)),
    };

    internal static CallerCredential? Credential(string name) => name switch
    {
        "USER" => CallerCredential.User,
        "ORGANIZATION" => CallerCredential.Organization,
        "MACHINE" => CallerCredential.Machine,
        "SYSTEM" => CallerCredential.System,
        _ => null,
    };

    private static string? ReadObject(byte[] bytes, Members members)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2 });

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return "It is not a JSON object.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                return "It is not a flat JSON object.";
            }

            var name = reader.GetString()!;
            if (!seen.Add(name))
            {
                return $"'{Shown(name)}' appears more than once.";
            }

            if (!reader.Read())
            {
                return "It ends early.";
            }

            var problem = ReadMember(ref reader, name, members);
            if (problem is not null)
            {
                return problem;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            return "It is not one JSON object.";
        }

        return null;
    }

    private static string? ReadMember(ref Utf8JsonReader reader, string name, Members members)
    {
        switch (name)
        {
            case VersionMember:
                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var number))
                {
                    return $"'{VersionMember}' is not a whole number.";
                }

                members.Version = number;
                return null;

            case ClientIpMember:
            case DeviceTypeMember:
            case DeviceIdentifierMember:
            case CredentialMember:
            case RecordedUserMember:
            case RecordedProviderMember:
                if (reader.TokenType != JsonTokenType.String)
                {
                    return $"'{name}' is not a string.";
                }

                members.Strings[name] = reader.GetString();
                return null;

            case OrganizationsMember:
                return ReadOrganizations(ref reader, members);

            default:
                return $"'{Shown(name)}' is not a member of version {Version}.";
        }
    }

    private static string? ReadOrganizations(ref Utf8JsonReader reader, Members members)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return $"'{OrganizationsMember}' is not an array.";
        }

        var organizations = new List<Guid>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String || !TryIdentifier(reader.GetString(), out var organization))
            {
                return $"'{OrganizationsMember}' holds something that is not an identifier.";
            }

            organizations.Add(organization);
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            return "It ends early.";
        }

        members.Organizations = organizations;
        return null;
    }

    private static string? ToValue(Members members, out CallerContextValue value)
    {
        value = new CallerContextValue();

        if (members.Version is not { } version)
        {
            return $"'{VersionMember}' is required.";
        }

        if (version != Version)
        {
            return $"Version {version.ToString(CultureInfo.InvariantCulture)} is not one this service reads.";
        }

        CallerCredential? credential = null;
        if (members.Strings.GetValueOrDefault(CredentialMember) is { } credentialName)
        {
            credential = Credential(credentialName);
            if (credential is null)
            {
                return $"'{CredentialMember}' is not USER, ORGANIZATION, MACHINE or SYSTEM.";
            }
        }

        if (!TryOptionalIdentifier(members.Strings.GetValueOrDefault(RecordedUserMember), out var recordedUser))
        {
            return $"'{RecordedUserMember}' is not an identifier.";
        }

        if (!TryOptionalIdentifier(members.Strings.GetValueOrDefault(RecordedProviderMember), out var recordedProvider))
        {
            return $"'{RecordedProviderMember}' is not an identifier.";
        }

        var clientIp = members.Strings.GetValueOrDefault(ClientIpMember);
        var candidate = new CallerContextValue
        {
            ClientIp = clientIp,
            DeviceType = members.Strings.GetValueOrDefault(DeviceTypeMember),
            DeviceIdentifier = members.Strings.GetValueOrDefault(DeviceIdentifierMember),
            Credential = credential,
            RecordedUser = recordedUser,
            RecordedProvider = recordedProvider,
            Organizations = members.Organizations,
        };

        var problem = Check(candidate);
        if (problem is not null)
        {
            return problem;
        }

        value = candidate with { ClientIp = clientIp is null ? null : IPAddress.Parse(clientIp).ToString() };
        return null;
    }

    private static string? Check(CallerContextValue value)
    {
        if (value.ClientIp is not null && !IPAddress.TryParse(value.ClientIp, out _))
        {
            return $"'{ClientIpMember}' is not an address.";
        }

        if (value.DeviceType is not null && !IsDeviceType(value.DeviceType))
        {
            return $"'{DeviceTypeMember}' is not a short name.";
        }

        if (value.DeviceIdentifier is not null && !IsDeviceIdentifier(value.DeviceIdentifier))
        {
            return $"'{DeviceIdentifierMember}' is not a device identifier.";
        }

        if (value.Credential is { } credential && !Enum.IsDefined(credential))
        {
            return $"'{CredentialMember}' is not a credential.";
        }

        if (value.RecordedUser is { } recorded
            && (recorded == Guid.Empty || value.Credential != CallerCredential.System))
        {
            return $"'{RecordedUserMember}' is carried only on a SYSTEM call, naming a person.";
        }

        if (value.RecordedProvider == Guid.Empty)
        {
            return $"'{RecordedProviderMember}' does not name a provider.";
        }

        if (value.Organizations is { } organizations)
        {
            if (organizations.Count > MaximumOrganizations)
            {
                return $"'{OrganizationsMember}' lists more than {MaximumOrganizations} organizations.";
            }

            if (organizations.Contains(Guid.Empty) || organizations.Distinct().Count() != organizations.Count)
            {
                return $"'{OrganizationsMember}' names an empty or repeated organization.";
            }
        }

        return null;
    }

    private static bool TryIdentifier(string? text, out Guid identifier) =>
        Guid.TryParseExact(text, "D", out identifier) && identifier != Guid.Empty;

    private static bool TryOptionalIdentifier(string? text, out Guid? identifier)
    {
        identifier = null;
        if (text is null)
        {
            return true;
        }

        if (!TryIdentifier(text, out var parsed))
        {
            return false;
        }

        identifier = parsed;
        return true;
    }

    private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string Shown(string name) =>
        name.Length <= 32 && MemberNamePattern().IsMatch(name) ? name : "a member";

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,50}\z")]
    private static partial Regex DeviceTypePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_:-]{1,50}\z")]
    private static partial Regex DeviceIdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_]+\z")]
    private static partial Regex MemberNamePattern();

    private sealed class Members
    {
        public int? Version { get; set; }

        public Dictionary<string, string?> Strings { get; } = new(StringComparer.Ordinal);

        public List<Guid>? Organizations { get; set; }
    }
}
