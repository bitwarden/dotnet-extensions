using System.Linq.Expressions;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A foreign key a cached read declares a database cascade through, and why that leaves no entry stale.
/// </summary>
/// <param name="Dependent">The row the key belongs to.</param>
/// <param name="Properties">The key's properties, in order.</param>
/// <param name="Coverage">Why no entry is left stale.</param>
/// <param name="Reason">The declaration's reason, for its reader.</param>
internal sealed record CascadeDeclaration(Type Dependent, IReadOnlyList<string> Properties, CascadeCoverage Coverage, string Reason)
{
    /// <summary>
    /// The declaration of a cascade through <paramref name="foreignKey"/>: a property of the row
    /// (<c>link =&gt; link.GroupId</c>), or properties in order (<c>link =&gt; new { link.A, link.B }</c>).
    /// </summary>
    /// <exception cref="ArgumentException">The key isn't a property of the row, or the reason is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="coverage"/> is no <see cref="CascadeCoverage"/>.</exception>
    public static CascadeDeclaration Of<TDependent>(Expression<Func<TDependent, object?>> foreignKey, CascadeCoverage coverage, string reason)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(foreignKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!Enum.IsDefined(coverage))
        {
            throw new ArgumentOutOfRangeException(nameof(coverage), coverage, null);
        }

        var row = foreignKey.Parameters[0];
        var body = foreignKey.Body is UnaryExpression { NodeType: ExpressionType.Convert } boxed ? boxed.Operand : foreignKey.Body;
        IReadOnlyList<Expression> members = body is NewExpression composite ? composite.Arguments : [body];
        var properties = members
            .Select(member => member is MemberExpression { Member: System.Reflection.PropertyInfo property } read && read.Expression == row
                ? property.Name
                : throw new ArgumentException(
                    $"A cascade is declared by the foreign key's properties of {typeof(TDependent).Name}, not {member}.", nameof(foreignKey)))
            .ToList();

        return new CascadeDeclaration(typeof(TDependent), properties, coverage, reason);
    }
}
