namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Declares that a product service's operation accepts a <b>Send credential</b>: the token Identity
/// mints for whoever holds one Send's link, naming the Send in its <c>send_id</c> claim and no person.
/// </summary>
/// <remarks>
/// <para>
/// A product service accepts only a person's session by default, and refuses a token naming no
/// person <c>401</c> at authentication. A Send's recipient has no account: accessing a Send
/// (<c>POST /sends/access</c>) and downloading its file take this token alone, under a Send-only
/// policy, and read the Send from its signed claim. An operation carrying this attribute accepts it; every
/// other operation keeps refusing it.
/// </para>
/// <para>
/// The operation must say what the credential may do, and only that: its own
/// <see cref="AuthorizationAttribute"/> must name a scope (the Send scope) and forbid both a user and
/// an organization, so a person's session (which lacks the scope) and an organization's are refused
/// there. The service refuses to start otherwise. Which Send the caller may reach is the claim's,
/// read with <see cref="SendCredential.SendId"/>; the service that holds the Send refuses any other.
/// </para>
/// <para>
/// Recognised only in a product service; below it every caller is a service.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AcceptsSendCredentialAttribute : Attribute;
