using System.Reflection;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Documents the default length cap that <see cref="Endpoints.ModelValidation.DefaultStringMaxLength"/> enforces,
/// and the bound an <see cref="EncryptedAttribute"/> declares in its place.
/// </summary>
/// <remarks>
/// The cap is applied through MVC's validation metadata, which the OpenAPI generator does not read,
/// so it has to be stated in the schema separately or the document would omit a constraint the
/// service enforces. The same is true of <see cref="EncryptedAttribute"/>, which the generator does
/// not know: without stating its bound here, an encrypted member would be published with the
/// default, documenting a limit the service does not apply, and one every encrypted value breaks.
/// </remarks>
internal sealed class DefaultStringLengthSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!typeof(Resource).IsAssignableFrom(context.JsonTypeInfo.Type) || schema.Properties is null)
        {
            return Task.CompletedTask;
        }

        var encrypted = EncryptedMembers(context);

        foreach (var (name, property) in schema.Properties)
        {
            // id and type belong to Resource, not to the resource's attributes: one is an
            // identifier and the other is pinned to a single value, so neither is a free-form
            // string a length would say anything about.
            if (name is ResourceEnvelope.Id or ResourceEnvelope.Type || property is not OpenApiSchema concrete)
            {
                continue;
            }

            if (encrypted.TryGetValue(name, out var declared))
            {
                concrete.MaxLength = declared;
            }
            else if (NeedsDefaultLength(concrete))
            {
                concrete.MaxLength = Endpoints.ModelValidation.DefaultStringMaxLength;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The resource's members that declare themselves encrypted, by their name on the wire, with
    /// the bound each declares.
    /// </summary>
    private static Dictionary<string, int> EncryptedMembers(OpenApiSchemaTransformerContext context)
    {
        var members = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            if (property.AttributeProvider is MemberInfo member
                && member.GetCustomAttribute<EncryptedAttribute>(inherit: true) is { } declaration)
            {
                declaration.EnsureBounded();
                members[property.Name] = declaration.MaxLength;
            }
        }

        return members;
    }

    /// <summary>
    /// Whether a member is a free-form string with no length of its own.
    /// </summary>
    /// <remarks>
    /// A member already constrained by an enumeration or a format (a UUID, a timestamp) is
    /// bounded by that constraint, and a length alongside it says nothing.
    /// </remarks>
    private static bool NeedsDefaultLength(OpenApiSchema property) =>
        property.Type?.HasFlag(JsonSchemaType.String) == true
        && property.MaxLength is null
        && property.Format is null
        && (property.Enum is null || property.Enum.Count == 0);
}
