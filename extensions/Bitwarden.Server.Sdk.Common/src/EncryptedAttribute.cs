using System.ComponentModel.DataAnnotations;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Declares that a string member holds Protected Data, ciphertext that only a client can read, and
/// how long it may be.
/// </summary>
/// <remarks>
/// <para>
/// An encrypted string carries its encryption type, an initialization vector, the ciphertext and a
/// MAC, so even a short secret is longer than a typical default string limit. This attribute
/// replaces that limit with an explicit bound, which only the author of the resource can choose.
/// </para>
/// <para>
/// Nothing is decrypted. The length is checked, and, with <see cref="WellFormed"/>, the shape: an
/// encrypted string's type, pieces and base64 (<see cref="EncryptedStringFormat"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Encrypted(MaxLength = 1_000_000)]
/// public required string Data { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class EncryptedAttribute : ValidationAttribute
{
    /// <summary>
    /// The longest value accepted, in characters. Must be positive.
    /// </summary>
    public int MaxLength { get; set; }

    /// <summary>
    /// Whether the value must also be shaped like an encrypted string (<see cref="EncryptedStringFormat"/>).
    /// A malformed value is refused as "{name} is not a valid encrypted string.", the words the Bitwarden
    /// server uses for the same refusal.
    /// </summary>
    public bool WellFormed { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// A null value is valid. Whether a member may be absent is left to its nullability.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><see cref="MaxLength"/> is not positive.</exception>
    public override bool IsValid(object? value)
    {
        EnsureBounded();

        return value switch
        {
            null => true,
            string text => text.Length <= MaxLength
                && (!WellFormed || EncryptedStringFormat.IsWellFormed(text)),
            _ => false,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// A malformed value is refused in the shape's words, an over-long one in the length's.
    /// </remarks>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (IsValid(value))
        {
            return ValidationResult.Success;
        }

        string[]? members = validationContext.MemberName is { } member ? [member] : null;
        var message = WellFormed && value is string text && !EncryptedStringFormat.IsWellFormed(text)
            ? FormatMalformedMessage(validationContext.DisplayName)
            : FormatErrorMessage(validationContext.DisplayName);
        return new ValidationResult(message, members);
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        $"The field {name} must be an encrypted string of at most {MaxLength} characters.";

    /// <summary>
    /// The message refusing a value that is not shaped like an encrypted string.
    /// </summary>
    /// <param name="name">The member's name.</param>
    public static string FormatMalformedMessage(string name) => $"{name} is not a valid encrypted string.";

    /// <summary>
    /// Refuses a declaration that states no bound.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="MaxLength"/> is not positive.</exception>
    public void EnsureBounded()
    {
        if (MaxLength <= 0)
        {
            throw new InvalidOperationException(
                $"[Encrypted] needs an explicit, positive {nameof(MaxLength)}; {MaxLength} is not one.");
        }
    }
}
