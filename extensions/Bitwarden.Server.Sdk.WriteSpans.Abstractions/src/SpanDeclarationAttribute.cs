namespace Bitwarden.Server.Sdk.WriteSpans;

/// <summary>
/// What an operation whose writes span services says about a failure part way: how it handles one
/// (<see cref="SpansServicesAttribute"/>), or that it doesn't yet (<see cref="SpansServicesUnhandledAttribute"/>).
/// </summary>
/// <remarks>
/// One per operation. <see cref="ServiceWrites"/> reads either through this type.
/// </remarks>
public abstract class SpanDeclarationAttribute : Attribute
{
    /// <summary>
    /// Initializes the declaration.
    /// </summary>
    /// <param name="reason">Why, in plain words.</param>
    /// <param name="empty">What a blank reason is refused with.</param>
    private protected SpanDeclarationAttribute(string reason, string empty)
    {
        Reason = !string.IsNullOrWhiteSpace(reason) ? reason : throw new ArgumentException(empty, nameof(reason));
    }

    /// <summary>
    /// The declaration's reason.
    /// </summary>
    public string Reason { get; }
}
