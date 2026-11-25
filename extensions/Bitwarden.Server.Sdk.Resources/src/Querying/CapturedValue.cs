using System.Linq.Expressions;

namespace Bitwarden.Server.Sdk.Resources.Querying;

/// <summary>
/// A value placed in an expression tree as a provider sends it: as a parameter.
/// </summary>
/// <remarks>
/// Read through a closure rather than inlined as a constant, so the provider sends it as a
/// parameter: one query plan whatever the value, and no value spliced into SQL text. The shape is
/// the one the compiler gives a lambda that captures a local, which is what providers look for.
/// </remarks>
internal static class CapturedValue
{
    /// <summary>
    /// An expression reading a value through a closure.
    /// </summary>
    /// <param name="value">The value, which must be assignable to <paramref name="type"/>.</param>
    /// <param name="type">The type the expression has.</param>
    /// <returns>A member access on a captured holder of the value.</returns>
    public static MemberExpression Of(object? value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(Holder<>).MakeGenericType(type), value)!;

        return Expression.Property(Expression.Constant(holder), nameof(Holder<int>.Value));
    }

    private sealed class Holder<T>(T value)
    {
        public T Value { get; } = value;
    }
}
