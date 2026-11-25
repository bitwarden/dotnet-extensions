namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// A message waiting to be sent: written in the same transaction as the change that sends it, and
/// deleted once the broker has accepted it.
/// </summary>
/// <remarks>
/// The body is the message's serialized versions, as <c>SendAsync</c> made them, so it holds only
/// what the message itself does: identifiers and the minimum facts the receiver needs, never Vault
/// Data, Authentication Tokens, key material or other secrets. The row is Data at Rest in the
/// sending service's own database and readable only by it. Times are Unix milliseconds, which
/// compare the same way on every database engine.
/// </remarks>
public sealed class OutboxMessage
{
    /// <summary>
    /// The message's identifier, which every send of it keeps.
    /// </summary>
    public string MessageId { get; set; } = null!;

    /// <summary>
    /// The topic or command queue it goes to.
    /// </summary>
    public string Destination { get; set; } = null!;

    /// <summary>
    /// Its message type (the payload's wire name), such as <c>organization.deleted</c>.
    /// </summary>
    public string MessageType { get; set; } = null!;

    /// <summary>
    /// The serialized versions.
    /// </summary>
    public byte[] Body { get; set; } = null!;

    /// <summary>
    /// The W3C traceparent of the code that sent it, so its send continues that trace.
    /// </summary>
    public string? TraceParent { get; set; }

    /// <summary>
    /// When it was written.
    /// </summary>
    public long CreatedAt { get; set; }

    /// <summary>
    /// When it first became due: when it was written, or the time <c>SendLaterAsync</c> was given.
    /// Retries don't move it, so how long a message has waited past it is how late it is.
    /// </summary>
    public long DueAt { get; set; }

    /// <summary>
    /// The earliest time it may be sent: <see cref="DueAt"/>, pushed later by each failed attempt.
    /// </summary>
    public long AvailableAt { get; set; }

    /// <summary>
    /// How many sends have failed.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Until when an instance's claim on it lasts.
    /// </summary>
    public long? LeaseUntil { get; set; }

    /// <summary>
    /// The claim's token.
    /// </summary>
    public Guid? LeaseToken { get; set; }

    /// <summary>
    /// The type of the last failure, never its message, which could carry the message's contents.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// When it was set aside: a failure that retrying can't fix happened too often. It stays for
    /// investigation and is no longer sent.
    /// </summary>
    public long? SetAsideAt { get; set; }
}
