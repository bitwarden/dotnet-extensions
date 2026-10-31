using System.Text;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Names the document's sections for a reader rather than for the compiler.
/// </summary>
/// <remarks>
/// <para>
/// The class declaring a handler names its tag, without an <c>Endpoints</c> suffix, so
/// <c>ProviderUsersEndpoints</c> publishes a section called <c>ProviderUsers</c>. That is an
/// identifier, not a heading. Split into words it reads as somebody would say it, and a service
/// can override the split or replace the name outright with <see cref="ApiTagAttribute"/>.
/// </para>
/// <para>
/// The same pass carries each section's description onto the document's tag list, which is the
/// only place OpenAPI has for explaining a group of operations rather than one of them.
/// </para>
/// </remarks>
internal sealed class TagDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var renamed = new Dictionary<string, OpenApiTag>(StringComparer.Ordinal);

        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            // A standard endpoint's handler class, which StandardEndpointApiDescriptionProvider
            // names the operation's "controller" route value after, and so its tag.
            var (name, type) = description.ActionDescriptor switch
            {
                var handler when handler.EndpointMetadata?.OfType<Endpoints.StandardEndpointMetadata>().Any() == true
                    && Endpoints.OperationMethods.Of(handler)?.DeclaringType is { } declaring
                    && handler.RouteValues.TryGetValue("controller", out var named) && named is not null
                    => (named, declaring),
                _ => (null, null),
            };

            if (name is null || type is null)
            {
                continue;
            }

            var declared = type
                .GetCustomAttributes(typeof(ApiTagAttribute), inherit: false)
                .OfType<ApiTagAttribute>()
                .FirstOrDefault();

            renamed[name] = new OpenApiTag
            {
                Name = declared?.Name ?? Words(name),
                Description = declared?.Description,
            };
        }

        Share(renamed);
        Retag(document, renamed);
        Declare(document, renamed);

        return Task.CompletedTask;
    }

    /// <remarks>
    /// Several endpoints classes may publish one section (an <c>[ApiTag("Passkeys")]</c> beside
    /// <c>PasskeysEndpoints</c>), and the description is written once, on whichever of them
    /// owns the concept. Without this the section's description would be whichever class
    /// happened to be declared last.
    /// </remarks>
    private static void Share(Dictionary<string, OpenApiTag> renamed)
    {
        var described = renamed.Values
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Description))
            .GroupBy(tag => tag.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Description, StringComparer.Ordinal);

        foreach (var tag in renamed.Values)
        {
            tag.Description ??= described.GetValueOrDefault(tag.Name ?? string.Empty);
        }
    }

    /// <remarks>
    /// Operations first. A tag a documentation UI groups by is the one on the operation; the
    /// document's own list only describes them, and a description attached to a name nothing
    /// carries is invisible.
    /// </remarks>
    private static void Retag(OpenApiDocument document, Dictionary<string, OpenApiTag> renamed)
    {
        var operations = document.Paths?.Values
            .Where(path => path.Operations is not null)
            .SelectMany(path => path.Operations!.Values) ?? [];

        foreach (var operation in operations)
        {
            if (operation.Tags is not { Count: > 0 })
            {
                continue;
            }

            var tags = operation.Tags
                .Select(tag => tag.Name is { } name && renamed.TryGetValue(name, out var replacement)
                    && replacement.Name is { } published
                    ? new OpenApiTagReference(published, document)
                    : tag)
                .ToArray();

            operation.Tags.Clear();

            foreach (var tag in tags)
            {
                operation.Tags.Add(tag);
            }
        }
    }

    /// <remarks>
    /// Merged rather than replaced. Not every endpoint is a standard endpoint (the platform's
    /// own health and readiness routes are not), and a tag only those carry would be dropped by
    /// a rewrite that only knows about standard endpoints. Dropping it does not hide the section,
    /// which a UI groups from the operations, but it does lose the declaration the section's
    /// description would hang on.
    /// </remarks>
    private static void Declare(OpenApiDocument document, Dictionary<string, OpenApiTag> renamed)
    {
        var published = renamed.Values.Select(tag => tag.Name).ToHashSet(StringComparer.Ordinal);

        var kept = document.Tags?
            .Where(tag => !renamed.ContainsKey(tag.Name ?? string.Empty)
                          && !published.Contains(tag.Name))
            .ToArray() ?? [];

        // The platform's own sections are described here, since no endpoints class carries them.
        foreach (var tag in kept.Where(tag => tag.Name == SystemEndpoints.Tag))
        {
            tag.Description ??= SystemEndpoints.TagDescription;
        }

        document.Tags = new HashSet<OpenApiTag>();

        foreach (var tag in renamed.Values.Concat(kept).DistinctBy(tag => tag.Name, StringComparer.Ordinal))
        {
            document.Tags.Add(tag);
        }
    }

    /// <summary>
    /// Splits a type name into words: <c>ProviderUsers</c> to "Provider Users".
    /// </summary>
    /// <remarks>
    /// A run of capitals is one word, so <c>SsoConfigs</c> gives "Sso Configs" rather than
    /// "S S O Configs"; still wrong for an acronym, which is what the attribute is for.
    /// </remarks>
    private static string Words(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var follows = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
            var endsRun = i > 0 && i + 1 < name.Length
                && char.IsUpper(name[i - 1]) && char.IsUpper(name[i]) && char.IsLower(name[i + 1]);

            if (char.IsUpper(name[i]) && (follows || endsRun))
            {
                builder.Append(' ');
            }

            builder.Append(name[i]);
        }

        return builder.ToString();
    }
}
