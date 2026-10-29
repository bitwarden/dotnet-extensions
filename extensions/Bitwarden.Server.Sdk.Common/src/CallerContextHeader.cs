using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The kind of credential the request that started a chain of calls presented, as the service
/// that validated it saw it.
/// </summary>
/// <remarks>
/// Audit-only. It says how an action was taken, for the record; no authorization, row filter or
/// cache reads it. Written ALL_CAPS on the wire, as every enumerated value Bitwarden's APIs write.
/// </remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(CallerCredentialJsonConverter))]
public enum CallerCredential
{
    /// <summary>A person's session.</summary>
    User = 1,

    /// <summary>An organization's API key, acting as the organization with nobody behind it.</summary>
    Organization,

    /// <summary>A Secrets Manager machine account's access token.</summary>
    Machine,

    /// <summary>
    /// The calling service itself, with no person acting: system operations
    /// (<c>OutgoingCall.AsSystem</c> in the Client package).
    /// </summary>
    System,
}

/// <summary>
/// What <see cref="BitwardenHeaders.Context"/> carries: version 1 of a closed object.
/// </summary>
/// <remarks>
/// <para>
/// A member is here only if the callee can't derive it, it says who acted or where the request
/// came from (never a fact about the subject), more than one operation reads it, and it declares
/// its source and use:
/// </para>
/// <list type="bullet">
/// <item><c>clientIp</c>: the address the originating request came from, taken by the product service
/// from its connection through the proxies it trusts. Read for audit records, security mail and
/// access rules that allowlist client addresses, which authorize on it.</item>
/// <item><c>deviceType</c>: the kind of client, from the client's <c>Device-Type</c> header or the session's
/// claim. Read for audit records and security mail.</item>
/// <item><c>deviceIdentifier</c>: the client's own identifier for its device, from the session's signed
/// <c>device</c> claim, then the client's <c>Device-Identifier</c> header. Read to spare that device a push
/// of what it did itself.</item>
/// <item><c>credential</c>: the kind of credential the request presented (<see cref="CallerCredential"/>),
/// set by the product service from the token it validated, or <c>SYSTEM</c> by
/// <c>OutgoingCall.AsSystem</c>. Audit-only.</item>
/// <item><c>recordedUser</c>: on a <c>SYSTEM</c> call only, the person the audit record names as having
/// acted, where it names one (<c>OutgoingCall.AsSystem(organization, recordedAs:)</c>). Audit-only.</item>
/// <item><c>recordedProvider</c>: the provider whose staff member is acting in one of its client
/// organizations, which the organization's events record beside them (their <c>ProviderId</c>), set by
/// the product service from the staff member's session. Their calls about the client name no provider, so
/// this is how the services they reach know it. Audit-only: never an authority.</item>
/// <item><c>organizations</c>: every organization the acting person is a confirmed member of, by identifier, from
/// the <c>organizations</c> claim Identity lists on their access token, set by the product
/// service and carried only on calls made as that person. Read to evaluate feature flags below the product layer,
/// where they are set on the LaunchDarkly user context. Targeting-only: never an
/// authority, a row filter or an audit fact. At most <see cref="CallerContextHeader.MaximumOrganizations"/>.</item>
/// </list>
/// <para>
/// The value is not signed: a callee trusts it exactly as far as it trusts the authenticated service
/// that sent it. Signed delegation (RFC 8693) could replace it later, keeping these members.
/// </para>
/// </remarks>
public sealed record CallerContextValue
{
    /// <summary>The address the originating request came from, canonical.</summary>
    public string? ClientIp { get; init; }

    /// <summary>The kind of client the originating request was made from.</summary>
    public string? DeviceType { get; init; }

    /// <summary>The device the originating request was made from.</summary>
    public string? DeviceIdentifier { get; init; }

    /// <summary>The kind of credential the originating request presented.</summary>
    public CallerCredential? Credential { get; init; }

    /// <summary>On a <see cref="CallerCredential.System"/> call, the person the audit record names as having acted.</summary>
    public Guid? RecordedUser { get; init; }

    /// <summary>The provider whose staff member is acting in one of its client organizations.</summary>
    public Guid? RecordedProvider { get; init; }

    /// <summary>The organizations the acting person is a confirmed member of, for feature-flag targeting only.</summary>
    public IReadOnlyList<Guid>? Organizations { get; init; }

    /// <summary>Whether there is nothing to carry.</summary>
    public bool IsEmpty =>
        ClientIp is null && DeviceType is null && DeviceIdentifier is null && Credential is null && RecordedUser is null
        && RecordedProvider is null && Organizations is not { Count: > 0 };
}

/// <summary>
/// Writes and reads <see cref="BitwardenHeaders.Context"/>.
/// </summary>
/// <remarks>
/// <para>
/// One definition for both ends, so what the outgoing handler writes is exactly what the callee
/// accepts. Reading is strict on purpose: the value is a closed object, and a member nobody defined,
/// a member twice, a value of the wrong kind or shape, or a header longer than
/// <see cref="MaximumLength"/> bytes is refused rather than partly believed. A value that would be
/// dropped as unsafe to record (an address that isn't one, a device type that is a paragraph) is
/// refused the same way: only the platform writes this header, so a malformed one is a defect or a
/// forgery, never a client being careless.
/// </para>
/// </remarks>
public static partial class CallerContextHeader
{
    /// <summary>The version this writes and the only one it reads.</summary>
    public const int Version = 1;

    /// <summary>The longest value accepted, in bytes: room for <see cref="MaximumOrganizations"/> and every other member.</summary>
    public const int MaximumLength = 16384;

    /// <summary>
    /// The most organizations <c>organizations</c> lists. A person in more is carried without the member, so their
    /// flags below the product layer are evaluated as for a person in none, rather than for an arbitrary part of them.
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

    /// <summary>Writes a value.</summary>
    /// <param name="value">What to carry.</param>
    /// <returns>
    /// The header's value: compact JSON, <c>v</c> first, absent members omitted. Never empty: with
    /// nothing else to carry it is <c>{"v":1}</c>, so every call between services says which
    /// version it speaks.
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

    /// <summary>Reads a value strictly.</summary>
    /// <param name="header">The header's value.</param>
    /// <param name="value">What it carries, when it is well formed.</param>
    /// <param name="problem">Why it was refused, otherwise: a sentence naming the member, never echoing a value.</param>
    /// <returns>Whether it is well formed.</returns>
    public static bool TryParse(string? header, out CallerContextValue value, out string? problem)
    {
        value = new CallerContextValue();
        problem = null;

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
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 2 });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                problem = "It is not a JSON object.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int? version = null;
            string? clientIp = null, deviceType = null, deviceIdentifier = null, credential = null, recordedUser = null,
                recordedProvider = null;
            List<Guid>? organizations = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    problem = "It is not a flat JSON object.";
                    return false;
                }

                var name = reader.GetString()!;
                if (!seen.Add(name))
                {
                    problem = $"'{Shown(name)}' appears more than once.";
                    return false;
                }

                if (!reader.Read())
                {
                    problem = "It ends early.";
                    return false;
                }

                switch (name)
                {
                    case VersionMember:
                        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var number))
                        {
                            problem = $"'{VersionMember}' is not a whole number.";
                            return false;
                        }

                        version = number;
                        break;

                    case ClientIpMember:
                    case DeviceTypeMember:
                    case DeviceIdentifierMember:
                    case CredentialMember:
                    case RecordedUserMember:
                    case RecordedProviderMember:
                        if (reader.TokenType != JsonTokenType.String)
                        {
                            problem = $"'{name}' is not a string.";
                            return false;
                        }

                        var text = reader.GetString();
                        switch (name)
                        {
                            case ClientIpMember: clientIp = text; break;
                            case DeviceTypeMember: deviceType = text; break;
                            case DeviceIdentifierMember: deviceIdentifier = text; break;
                            case CredentialMember: credential = text; break;
                            case RecordedUserMember: recordedUser = text; break;
                            default: recordedProvider = text; break;
                        }

                        break;

                    case OrganizationsMember:
                        if (reader.TokenType != JsonTokenType.StartArray)
                        {
                            problem = $"'{OrganizationsMember}' is not an array.";
                            return false;
                        }

                        organizations = [];
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType != JsonTokenType.String
                                || !Guid.TryParseExact(reader.GetString(), "D", out var organization)
                                || organization == Guid.Empty)
                            {
                                problem = $"'{OrganizationsMember}' holds something that is not an identifier.";
                                return false;
                            }

                            organizations.Add(organization);
                        }

                        if (reader.TokenType != JsonTokenType.EndArray)
                        {
                            problem = "It ends early.";
                            return false;
                        }

                        break;

                    default:
                        problem = $"'{Shown(name)}' is not a member of version {Version}.";
                        return false;
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                problem = "It is not one JSON object.";
                return false;
            }

            if (version is null)
            {
                problem = $"'{VersionMember}' is required.";
                return false;
            }

            if (version != Version)
            {
                problem = $"Version {version.Value.ToString(CultureInfo.InvariantCulture)} is not one this service reads.";
                return false;
            }

            CallerCredential? parsedCredential = null;
            if (credential is not null)
            {
                if (Credential(credential) is not { } known)
                {
                    problem = $"'{CredentialMember}' is not USER, ORGANIZATION, MACHINE or SYSTEM.";
                    return false;
                }

                parsedCredential = known;
            }

            Guid? parsedRecordedUser = null;
            if (recordedUser is not null)
            {
                if (!Guid.TryParseExact(recordedUser, "D", out var person) || person == Guid.Empty)
                {
                    problem = $"'{RecordedUserMember}' is not an identifier.";
                    return false;
                }

                parsedRecordedUser = person;
            }

            Guid? parsedRecordedProvider = null;
            if (recordedProvider is not null)
            {
                if (!Guid.TryParseExact(recordedProvider, "D", out var provider) || provider == Guid.Empty)
                {
                    problem = $"'{RecordedProviderMember}' is not an identifier.";
                    return false;
                }

                parsedRecordedProvider = provider;
            }

            var candidate = new CallerContextValue
            {
                ClientIp = clientIp,
                DeviceType = deviceType,
                DeviceIdentifier = deviceIdentifier,
                Credential = parsedCredential,
                RecordedUser = parsedRecordedUser,
                RecordedProvider = parsedRecordedProvider,
                Organizations = organizations,
            };

            problem = Check(candidate);
            if (problem is not null)
            {
                return false;
            }

            value = candidate with { ClientIp = clientIp is null ? null : IPAddress.Parse(clientIp).ToString() };
            return true;
        }
        catch (JsonException)
        {
            problem = "It is not well-formed JSON.";
            return false;
        }
    }

    /// <summary>A device type, held to a short name: letters, digits, hyphens and underscores.</summary>
    /// <param name="value">The value.</param>
    public static bool IsDeviceType(string? value) => value is not null && DeviceTypePattern().IsMatch(value);

    /// <summary>
    /// A device identifier, held to what Bitwarden allows in a push tag (letters, digits,
    /// <c>-</c>, <c>_</c> and <c>:</c>) and to its 50-character device identifier column.
    /// </summary>
    /// <param name="value">The value.</param>
    public static bool IsDeviceIdentifier(string? value) => value is not null && DeviceIdentifierPattern().IsMatch(value);

    /// <summary>The wire name of a credential: ALL_CAPS.</summary>
    /// <param name="credential">The credential.</param>
    public static string Name(CallerCredential credential) => credential switch
    {
        CallerCredential.User => "USER",
        CallerCredential.Organization => "ORGANIZATION",
        CallerCredential.Machine => "MACHINE",
        CallerCredential.System => "SYSTEM",
        _ => throw new ArgumentOutOfRangeException(nameof(credential)),
    };

    /// <summary>The credential a wire name names, or null for anything else.</summary>
    /// <param name="name">The ALL_CAPS name.</param>
    internal static CallerCredential? Credential(string name) => name switch
    {
        "USER" => CallerCredential.User,
        "ORGANIZATION" => CallerCredential.Organization,
        "MACHINE" => CallerCredential.Machine,
        "SYSTEM" => CallerCredential.System,
        _ => null,
    };

    /// <summary>What is wrong with a value's members, or null.</summary>
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

    private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    /// <summary>A member's name, as a refusal may show it: short, and only the characters a name has.</summary>
    private static string Shown(string name) =>
        name.Length <= 32 && MemberNamePattern().IsMatch(name) ? name : "a member";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex DeviceTypePattern();

    [GeneratedRegex("^[A-Za-z0-9_:-]{1,50}$")]
    private static partial Regex DeviceIdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex MemberNamePattern();
}

/// <summary>Writes and reads <see cref="CallerCredential"/> by its ALL_CAPS name, wherever it is serialized.</summary>
public sealed class CallerCredentialJsonConverter : System.Text.Json.Serialization.JsonConverter<CallerCredential>
{
    /// <inheritdoc />
    public override CallerCredential Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && CallerContextHeader.Credential(reader.GetString()!) is { } credential
            ? credential
            : throw new JsonException("Not USER, ORGANIZATION, MACHINE or SYSTEM.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CallerCredential value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(CallerContextHeader.Name(value));
    }
}
