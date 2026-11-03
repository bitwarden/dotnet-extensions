# API paths

The standard asks for lowercase kebab-case paths made of a namespace (`api`), a version (`v1`), the
resource plural, and then an identifier and sub-resources, with the subject of a request in the path
and its scope (the current organization or user) left out of it. The package puts the standard
operations on that shape and gives a handler its scope from the request's context, but the path
itself is yours: it does not check the namespace, the version or the casing. The standard:
[API paths](https://contributing.bitwarden.com/architecture/server/apis/api-paths).

## What the package does

| The standard asks                                   | The package                                                                                                                                                                               |
| --------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `api`, then `v{n}`, then the resource plural        | Nothing enforced. You name the group (`MapGroup("api/v1/widgets")`). The standard attributes default to the shapes below it.                                                              |
| An identifier after the plural for one resource     | `[StandardReadOne]`, `[StandardUpdate]` and `[StandardDelete]` default to `{id}`; `[StandardCreate]` and `[StandardReadMany]` to the collection itself.                                   |
| Additional elements for sub-resources               | Any attribute takes a `template`, such as `[StandardReadMany("{id}/parts")]`. The `Map` line must use the same route, or the service refuses to start.                                    |
| Kebab case                                          | Generated segments are kebab case: an action's name (`actions/send-email`) and a bulk operation's suffix (`:bulk-delete`). Your own segments are not checked.                             |
| Each API versioned independently                    | The version is read from the route for the operation identifier: `getWidget` on `v1`, `getWidgetV2` on `v2`. Nothing requires a `v2` path's parent paths to exist.                        |
| The subject `MUST` be in the path                   | A route parameter binds to the handler's parameter of the same name (`string id`).                                                                                                        |
| The scope `MUST NOT` be in the path                 | The scope reaches the handler as `IContext` (organization, user, provider) and `Person`, from the session's token in a product service or the caller-context headers in a domain service. |
| What an operation requires of the scope is declared | `[Authorization(Organization = ..., User = ..., Provider = ...)]`, each `Required`, `Optional` or `Forbidden`, settled from the service, the endpoints class and the handler.             |

## Example

The widgets collection, one widget, the current person's widget, and a widget's parts:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using Bitwarden.Server.Sdk.Service.Querying;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    // Namespace, version, then the resource plural.
    public const string Collection = "api/v1/widgets";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup(Collection).WithStandards();
        widgets.MapGet("", ListWidgets);           // GET api/v1/widgets
        widgets.MapGet("current", GetMyWidget);    // GET api/v1/widgets/current
        widgets.MapGet("{id}", GetWidget);         // GET api/v1/widgets/{id}
        widgets.MapGet("{id}/parts", ListParts);   // GET api/v1/widgets/{id}/parts
    }

    /// <summary>
    /// Get the current person's widget
    /// </summary>
    [Authorization(User = ContextRequirement.Required)]
    [StandardReadOne("current")]
    public static async Task<ResourceDocument<Widget>> GetMyWidget(
        Person person, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindForPersonAsync(person.Id, cancellationToken)
            ?? throw new NotFoundException("widget", "current"));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindAsync(id, cancellationToken)
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// List a widget's parts
    /// </summary>
    [StandardReadMany("{id}/parts")]
    public static async Task<ResourceListDocument<WidgetPart>> ListParts(
        string id, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadManyResponse(await store.ListPartsAsync(id, cancellationToken));
}
```

`current` names whatever the scope names, so `GetMyWidget` takes the person from the context rather
than asking the caller to repeat an identifier they already sent. A handler taking `Person` must
require a user, or the service refuses to start.

### Which identifiers belong in the path

The subject goes in the path; the organization the request runs inside does not. A handler reads it
from `IContext` (Bitwarden.Server.Sdk.Common):

```csharp
/// <summary>
/// List widgets
/// </summary>
/// <remarks>
/// With an organization in context, that organization's widgets. Without one, the widgets of
/// every organization the person belongs to, or of the ones `filter[organization]` names.
/// </remarks>
/// <param name="organization">Only widgets in these organizations.</param>
/// <param name="context">The request's scope.</param>
/// <param name="person">The person making the request.</param>
/// <param name="store">Where widgets are kept.</param>
/// <param name="cancellationToken">Cancels the request.</param>
[Authorization(Organization = ContextRequirement.Optional, User = ContextRequirement.Required)]
[StandardReadMany]
public static async Task<ResourceListDocument<Widget>> ListWidgets(
    Filter<List<Guid>>? organization,
    IContext context,
    Person person,
    IWidgetStore store,
    CancellationToken cancellationToken)
{
    // The scope comes from the context, never the path.
    if (context.OrganizationId is { } current)
    {
        return ReadManyResponse(await store.ListForOrganizationAsync(current, cancellationToken));
    }

    // A filter's values are the caller's to choose, so the store reads only organizations
    // the person belongs to. One they don't belong to is left out, never answered.
    return ReadManyResponse(await store.ListForMemberAsync(person.Id, organization?.Value, cancellationToken));
}
```

Where the organization comes from depends on the kind of service. A product service takes it from
the organization the person's session names; a domain service from the `Bitwarden-Organization`
header, believed only from another service's own token with a valid context token. Either way the
handler sees the same `IContext`.

### Working across several scopes

`ListWidgets` above is one operation declared so that less scope means more. Called with an
organization in context, it returns that organization's widgets. Called with only a person, it
returns the widgets of every organization they belong to, or of the ones a filter names:

```http
GET /api/v1/widgets?filter[organization]=0b7c2d5e-8f1a-4c3b-9d6e-2a4f8b1c7e3d,4e1a9c2b-6d3f-4b8e-a1c5-7f2e9d4b6a8c HTTP/1.1
```

The organizations stay out of the path either way. The other options the standard lists need nothing
from the package: making several calls with a different organization in context each time, or an
[advanced query](../querying/advanced-queries.md) whose body carries the list.

## What you decide

- **The path.** Choose the namespace, version and plural in `MapGroup`, and keep your own segments
  in lowercase kebab case. The package doesn't check them. To have the service refuse to start over
  a path that breaks the rule, register a check of your own (below).
- **Which parent paths exist.** Paths should be traversable, but a parent need not be callable. The
  package doesn't require one.
- **What a filter's values may reach.** `Filter<T>` parses `filter[organization]` but authorizes
  nothing: the values are the caller's. Read only the organizations the caller may reach, as
  `ListForMemberAsync` does above.
- **What the scope permits.** Declare it on the operation with `[Authorization]`. See
  [Authentication and authorization](../beyond-the-standards/authentication-and-authorization.md).

An `IStandardEndpointCheck` runs over every endpoint at startup and adds its problems to the
package's own:

```csharp
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Refuses to start a service with a route outside <c>api/v{n}/...</c> in lowercase kebab case.
/// </summary>
public sealed partial class WidgetPathCheck : IStandardEndpointCheck
{
    public void Check(IReadOnlyList<Endpoint> endpoints, ICollection<string> problems)
    {
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var route = endpoint.RoutePattern.RawText?.Trim('/') ?? string.Empty;

            // The documentation and the bare address are the platform's, outside the API.
            if (route.Length == 0 || route.StartsWith("docs", StringComparison.Ordinal) || route == "api/openapi.json")
            {
                continue;
            }

            if (!ApiPath().IsMatch(route))
            {
                problems.Add($"'{route}' is not api/v{{n}}/{{resource}} in lowercase kebab case.");
            }
        }
    }

    // api, a version, then kebab-case segments, route parameters, or a ':' operation suffix.
    [GeneratedRegex(@"^api/v[1-9][0-9]*(/([a-z0-9]+(-[a-z0-9]+)*|\{[^}]+\}))+(:[a-z0-9]+(-[a-z0-9]+)*)?$")]
    private static partial Regex ApiPath();
}
```

Register it in `ConfigureServices` with
`services.AddSingleton<IStandardEndpointCheck, WidgetPathCheck>()`. A route such as
`api/Widget_Stats` then stops the service:

```text
System.InvalidOperationException: The service's endpoints do not meet the standards:
  - 'api/Widget_Stats' is not api/v{n}/{resource} in lowercase kebab case.
```

## See also

- [OpenAPI](openapi.md): operation identifiers and the version suffix.
- [Breaking changes and versioning](breaking-changes-and-versioning.md): adding a `v2`.
- [Verbs](../operations/verbs.md): the standard attributes' routes and the startup check.
- [Filtering](../querying/filtering.md)
- [Caller context](../beyond-the-standards/caller-context.md)
- The standard: [API paths](https://contributing.bitwarden.com/architecture/server/apis/api-paths)
