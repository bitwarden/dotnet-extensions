namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Marks a member a caller may read but not set.
/// </summary>
/// <remarks>
/// Documented as <c>readOnly</c>, which the standards require of any server-set field. Named for
/// the contract rather than for one reason behind it: an audit timestamp, a derived total and a
/// computed status are all read-only, and only the first is meaningfully "server assigned".
/// <para>
/// Note that <c>System.ComponentModel.ReadOnlyAttribute</c> also exists. A file importing both
/// that namespace and this one must qualify the reference, which the compiler will insist on
/// rather than resolve quietly.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ReadOnlyAttribute : Attribute;
