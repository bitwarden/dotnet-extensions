using System.ComponentModel.DataAnnotations;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Declares that a string member holds Protected Data — ciphertext a client produced and only a
/// client can read — and how long it may be.
/// </summary>
/// <remarks>
/// <para>
/// A resource's string members are capped at a short default length unless they declare one,
/// which suits a name or a label and refuses every encrypted value: an encrypted string carries
/// its encryption type, an initialization vector, the ciphertext and a MAC, all base64, so even a
/// short secret is longer than a short label. This is how a member says it holds one, and states
/// the bound that replaces the default.
/// </para>
/// <para>
/// The bound is required and explicit. An encrypted value is still a request a caller fills in,
/// and an unbounded one is an unbounded request; the right maximum depends on what is encrypted —
/// a folder name is not an item's whole structure — so only the author of the resource can say.
/// </para>
/// <para>
/// <b>Nothing is decrypted or inspected.</b> The service holds no key that could read the value
/// and checks only its length, so declaring a member encrypted never brings its contents into
/// reach of the server.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// /// &lt;summary&gt;The item's encrypted structure.&lt;/summary&gt;
/// [Encrypted(MaxLength = 1_000_000)]
/// public required string Data { get; init; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class EncryptedAttribute : ValidationAttribute
{
    /// <summary>The longest value accepted, in characters. Must be positive.</summary>
    public int MaxLength { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// <see cref="MaxLength"/> was not set to a positive value. A declaration that states no
    /// bound is a mistake in the resource, not in the request, and is reported as one.
    /// </exception>
    public override bool IsValid(object? value)
    {
        EnsureBounded();

        return value switch
        {
            // Absence is the business of nullability, as it is for every other member.
            null => true,
            string text => text.Length <= MaxLength,
            _ => false,
        };
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        $"The field {name} must be an encrypted string of at most {MaxLength} characters.";

    /// <summary>Refuses a declaration that states no bound.</summary>
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
