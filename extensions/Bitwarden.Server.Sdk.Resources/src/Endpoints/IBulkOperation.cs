using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources;

/// <summary>
/// One of the standard bulk operations: what it declares about atomicity and how its resources
/// are named.
/// </summary>
/// <remarks>
/// The one place the bulk attributes are read, so the standard filter, its startup check and the
/// OpenAPI description cannot disagree about what an operation offers, and a new bulk form is
/// read the same way as soon as it implements this.
/// </remarks>
internal interface IBulkOperation : IStandardOperation
{
    /// <summary>
    /// The atomicity modes the operation offers.
    /// </summary>
    Atomicity Supports { get; }

    /// <summary>
    /// How the caller may name the resources. A create or replace carries its resources in
    /// <c>data</c> and has nothing to target, so it accepts either.
    /// </summary>
    Targeting Targeting { get; }
}
