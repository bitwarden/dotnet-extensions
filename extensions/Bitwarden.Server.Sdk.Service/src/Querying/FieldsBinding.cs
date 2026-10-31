
namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads a <see cref="Fields{T}"/> from <c>fields[type]</c>, for its <c>BindAsync</c>.
/// </summary>
internal static class FieldsBinding
{
    /// <summary>
    /// The sparse fieldset the query asks for, or every attribute.
    /// </summary>
    /// <param name="type">The <see cref="Fields{T}"/> type.</param>
    /// <param name="query">The request's query string.</param>
    /// <returns>The fieldset.</returns>
    /// <exception cref="InvalidFieldsException">A name that is not one of the resource's attributes.</exception>
    public static object Bind(Type type, Microsoft.AspNetCore.Http.IQueryCollection query)
    {
        var name = (string)type.GetProperty(nameof(Fields<JsonApi.Resource>.Parameter))!.GetValue(null)!;
        var supplied = query[name];

        if (supplied.Count == 0)
        {
            return Activator.CreateInstance(type)!;
        }

        // The last value, as the platform's other single-valued parameters take it.
        var names = (supplied[^1] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            return Activator.CreateInstance(type, [names.AsEnumerable()])!;
        }
        catch (System.Reflection.TargetInvocationException invocation) when (invocation.InnerException is InvalidFieldsException refused)
        {
            throw refused;
        }
    }
}
