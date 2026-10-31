using System.Text.Json.Serialization.Metadata;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Names the schemas a resource document produces.
/// </summary>
/// <remarks>
/// <para>
/// Left alone, the generator names a generic type after its arity and its argument:
/// <c>ResourceDocumentOfUser</c>, <c>ResourceListDocumentOfUser</c>. That leaks the platform's generics
/// into a contract that should be about the domain, and it is what a generated client's classes
/// end up called, in every language, for every consumer of the API.
/// </para>
/// <para>
/// <c>Document</c> is JSON:API's own word for the top-level object, so it is the contract's
/// vocabulary rather than invented. It also reads correctly in both directions: the same schema
/// describes a request body and a response, which is why it is not named <c>Response</c>.
/// </para>
/// </remarks>
internal static class SchemaNames
{
    /// <summary>
    /// Names a schema, deferring to the generator for anything but a document.
    /// </summary>
    /// <param name="type">The type being described.</param>
    /// <returns>The schema's name in <c>components/schemas</c>.</returns>
    public static string? For(JsonTypeInfo type)
    {
        var clr = type.Type;

        // Bound from query parameters, which the document describes individually: the page a
        // caller asks for, the filters it applies, the order it wants. None of these types is
        // ever part of a payload, and naming one puts a class in components that no operation
        // references and every generated client still has to carry.
        if (clr == typeof(Querying.Page)
            || clr == typeof(Querying.Sort)
            || clr == typeof(PageNumber)
            || clr == typeof(PageSize)
            || clr == typeof(PageAfter)
            || (clr.IsGenericType && clr.GetGenericTypeDefinition() == typeof(Filter<>))
            || (clr.IsGenericType && clr.GetGenericTypeDefinition() == typeof(Querying.Range<>)))
        {
            return null;
        }

        // Enumerations are inlined rather than referenced. A caller reading an operation wants
        // to see which values a field accepts, and a $ref to a named type means opening another
        // section to find out, for what is, on the wire, a string from a short list. It also
        // stops a generated client inventing a C# type for every one of them.
        if ((Nullable.GetUnderlyingType(clr) ?? clr).IsEnum)
        {
            return null;
        }

        if (!clr.IsGenericType)
        {
            return Default(type);
        }

        var definition = clr.GetGenericTypeDefinition();
        var resource = clr.GetGenericArguments()[0].Name;

        if (definition == typeof(ResourceDocument<>))
        {
            return $"{resource}Document";
        }

        if (definition == typeof(ResourceListDocument<>))
        {
            // Not "UsersDocument": one letter is too little to carry the difference between a
            // resource and a list of them, in a schema index or in a generated client.
            return $"{resource}ListDocument";
        }

        if (definition == typeof(PagedResourceListDocument<>))
        {
            // A separate name because it is a separate shape: only this one carries links and
            // a total. An operation's schema should say which it returns.
            return $"{resource}PagedListDocument";
        }

        if (definition == typeof(BulkCreatedDocument<>))
        {
            return $"{resource}BulkCreatedDocument";
        }

        if (definition == typeof(CursorPagedResourceListDocument<>))
        {
            // Not "…PagedListDocument": that suffix means page numbers and a total, and a
            // generated client would otherwise present the one as the other.
            return $"{resource}CursorListDocument";
        }

        return Default(type);
    }

    /// <remarks>
    /// The generator's own answer, not null. Returning null means "do not give this a reference",
    /// which inlines the schema wherever it is used, so a resource would stop appearing in
    /// <c>components/schemas</c> and a generated client would lose the class for it.
    /// </remarks>
    private static string? Default(JsonTypeInfo type) => OpenApiOptions.CreateDefaultSchemaReferenceId(type);
}
