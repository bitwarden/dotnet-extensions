using System.ComponentModel.DataAnnotations;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// An action's plain input.
/// </summary>
public sealed record ParityInput : IValidatableObject
{
    /// <summary>
    /// Required, being non-nullable.
    /// </summary>
    public string Subject { get; init; } = null!;

    /// <summary>
    /// Within a range, and never seven.
    /// </summary>
    [Range(1, 10)]
    public int Count { get; init; }

    /// <summary>
    /// An address, if given.
    /// </summary>
    [EmailAddress]
    public string? Email { get; init; }

    /// <summary>
    /// A nested object.
    /// </summary>
    public ParityAddress? Address { get; init; }

    /// <summary>
    /// A list of them.
    /// </summary>
    public List<ParityAddress>? Items { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Count == 7)
        {
            yield return new ValidationResult("Seven is not allowed.", [nameof(Count)]);
        }
    }
}
