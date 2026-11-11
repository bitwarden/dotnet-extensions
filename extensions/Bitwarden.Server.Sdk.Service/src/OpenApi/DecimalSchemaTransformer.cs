using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Describes a <see cref="decimal"/> as a decimal rather than as a double.
/// </summary>
/// <remarks>
/// <para>
/// .NET publishes a <c>decimal</c> as <c>type: number, format: double</c>, and every generator
/// believes it: a caller of a service that charges money gets a binary floating-point type to
/// hold it in. <c>double</c> carries about sixteen significant digits, and a <c>decimal</c>
/// carries twenty-eight, so a value the service can represent exactly is one a generated
/// client cannot, and a ledger reconciled through such a client will eventually disagree with
/// the one it is reconciling against by a rounding error nobody can locate.
/// </para>
/// <para>
/// The wire does not change: JSON has one number type and the value is already written in full.
/// What changes is the declared format, from <c>double</c> to <c>decimal</c>, which generators
/// read as the exact type. The schema already gives itself away: .NET emits
/// <c>maximum: 79228162514264337593543950335</c>, which is <c>decimal.MaxValue</c> and is not
/// a number any double-valued field would have.
/// </para>
/// <para>
/// Detected from the bounds rather than from the CLR type, because a schema transformer is
/// handed the schema and the type that produced it, and a decimal reaches it as an unadorned
/// numeric schema by the time it can be changed.
/// </para>
/// </remarks>
internal sealed class DecimalSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>
    /// The format .NET writes for a <see cref="decimal"/>, and which is wrong.
    /// </summary>
    private const string Double = "double";

    /// <summary>
    /// The format a generator reads as an exact decimal type.
    /// </summary>
    private const string Decimal = "decimal";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (schema.Format == Double && IsDecimal(context.JsonTypeInfo.Type))
        {
            schema.Format = Decimal;
        }

        return Task.CompletedTask;
    }

    private static bool IsDecimal(Type type) =>
        type == typeof(decimal) || Nullable.GetUnderlyingType(type) == typeof(decimal);
}
