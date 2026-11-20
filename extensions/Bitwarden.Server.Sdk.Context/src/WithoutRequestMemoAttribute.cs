namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// Gives the operation it marks no <see cref="RequestMemo"/>: every call its request makes to another service is sent,
/// however often the same one was made before.
/// </summary>
/// <remarks>
/// <para>
/// For an operation whose purpose is to see change while it runs: one that polls another service, or waits on another
/// service's event, until an answer moves. Everything else wants the memo, and one deliberate re-read is better made with
/// <c>OutgoingCall.Fresh()</c> around that call alone.
/// </para>
/// <para>
/// Put it on the handler, or add it to the endpoint's metadata (<c>.WithMetadata(new WithoutRequestMemoAttribute())</c>).
/// The service host reads it when the request is routed, before the memo would be begun.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Delegate, AllowMultiple = false, Inherited = true)]
public sealed class WithoutRequestMemoAttribute : Attribute
{
}
