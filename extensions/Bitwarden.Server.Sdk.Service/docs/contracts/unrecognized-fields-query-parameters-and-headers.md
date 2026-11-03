# Unrecognized fields, query parameters, and headers

The standard asks APIs to ignore fields, query parameters and headers they don't recognize, but to
refuse with `422` a parameter that would shape the result and that the API doesn't support. The
package ignores what it doesn't recognize, and refuses a shaping parameter that the operation
declares but can't honour. It does not refuse a shaping parameter the operation doesn't declare at
all: that one is ignored, so a service adds the check itself. The standard:
[Unrecognized fields, query parameters, and headers](https://contributing.bitwarden.com/architecture/server/apis/unrecognized-fields-query-parameters-and-headers).

## What the package does

| The standard asks                                         | The package                                                                                                                                                       |
| --------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Ignore an unrecognized field                              | A resource document's unknown members, inside `attributes` or beside them, are dropped before the handler sees the resource. Read-only members are dropped too.   |
| Ignore an unrecognized query parameter                    | Ignored. Only parameters the handler declares are bound.                                                                                                          |
| Ignore an unrecognized header                             | Ignored. Exception: a product service refuses the caller-context headers (`Bitwarden-Organization` and the rest) `400`, because only a domain service takes them. |
| Refuse `filter[name]` naming an unsupported field         | Not done. A `filter[name]` that no `Filter<T>` parameter named `name` declares is ignored.                                                                        |
| Refuse `sort` naming an unsupported field                 | Done when the operation takes `Sort`: a field the resource doesn't mark `[Sortable]` is refused `422`. Without a `Sort` parameter, `sort` is ignored.             |
| Refuse `fields[type]` naming an unsupported field         | Done when the operation takes `Fields<T>`: an attribute the resource doesn't have is refused `422`. Without a `Fields<T>` parameter, it is ignored.               |
| Refuse paging parameters the API doesn't support          | Not done. `page[number]` on an operation without `PageNumber`, or `page[after]` on one without `PageAfter`, is ignored.                                           |
| Refuse an `atomic` value a bulk operation doesn't support | Done. Every bulk operation checks `atomic` against its attribute's `Supports` and refuses anything else `422`.                                                    |

An unknown member is ignored:

```http
POST /api/v1/widgets HTTP/1.1
Content-Type: application/json

{"data":{"type":"widget","attributes":{"name":"Desk lamp","colour":"red","createdBy":"someone"}}}
```

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Location: /api/v1/widgets/2f0c4a8e-5b1d-4c6e-9a7f-3d2b1e0f9c8a

{"data":{"id":"2f0c4a8e-5b1d-4c6e-9a7f-3d2b1e0f9c8a","type":"widget","attributes":{"name":"Desk lamp"}}}
```

A field the resource doesn't mark sortable is refused, on an operation that takes `Sort`:

```http
GET /api/v1/widgets?sort=colour HTTP/1.1
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"'colour' cannot be used to sort: 'colour' is not a field this collection sorts by. Sortable fields are 'createdAt', 'name'.","source":{"parameter":"sort"}}]}
```

## Example

To refuse a shaping parameter the operation doesn't declare, add an
`IStandardEndpointFilterProvider`. The package asks it once for every standard endpoint and runs the
filter it returns after binding and validation, before the handler. This one reads which shaping
parameters the handler declares, and refuses any other:

```csharp
using System.Reflection;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Refuses a result-shaping query parameter (<c>filter[...]</c>, <c>sort</c>, <c>fields[...]</c>
/// or <c>page[...]</c>) that the operation does not declare, rather than ignoring it.
/// </summary>
public sealed class WidgetQueryShapeCheck : IStandardEndpointFilterProvider
{
    private static readonly string[] Shaping = ["filter[", "fields[", "page["];

    public IEndpointFilter? For(MethodInfo method, ICollection<string> problems)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in method.GetParameters())
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;

            if (definition == typeof(Filter<>))
            {
                declared.Add($"filter[{parameter.Name}]");
            }
            else if (definition == typeof(Fields<>))
            {
                declared.Add((string)type.GetProperty(nameof(Fields<>.Parameter))!.GetValue(null)!);
            }
            else if (type == typeof(Sort))
            {
                declared.Add(Sort.Parameter);
            }
            else if (type == typeof(PageNumber))
            {
                declared.Add(PageNumber.Parameter);
            }
            else if (type == typeof(PageSize))
            {
                declared.Add(PageSize.Parameter);
            }
            else if (type == typeof(PageAfter))
            {
                declared.Add(PageAfter.Parameter);
            }
        }

        return new Filter(declared);
    }

    private sealed class Filter(HashSet<string> declared) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            foreach (var name in context.HttpContext.Request.Query.Keys)
            {
                var shapes = name == Sort.Parameter || Shaping.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

                if (shapes && !declared.Contains(name))
                {
                    throw new UnsupportedQueryParameterException(name);
                }
            }

            return next(context);
        }
    }
}

/// <summary>
/// A query parameter that would shape the result, on an operation that doesn't support it.
/// </summary>
public sealed class UnsupportedQueryParameterException(string parameter)
    : Exception($"This operation does not support '{parameter}'."), IProvideErrors
{
    public IReadOnlyList<Error> Errors =>
    [
        new()
        {
            Status = "422",
            Code = ErrorCodes.InvalidValue,
            Title = ErrorTitles.InvalidValue,
            Detail = Message,
            Source = new ErrorSource { Parameter = parameter },
        },
    ];
}
```

Register it in `ConfigureServices`:

```csharp
services.AddSingleton<IStandardEndpointFilterProvider, WidgetQueryShapeCheck>();
```

An exception implementing `IProvideErrors` is answered with its own errors, so a misspelled filter
is now refused in the standard error document:

```http
GET /api/v1/widgets?filter[colour]=red HTTP/1.1
```

```http
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"422","code":"invalid-value","title":"A field has an invalid value.","detail":"This operation does not support 'filter[colour]'.","source":{"parameter":"filter[colour]"}}]}
```

`page[after]` on an offset-paged list, or `page[number]` on a cursor-paged one, is refused the same
way. A parameter that shapes nothing, such as `?trace=1`, is still ignored.

## What you decide

- **Whether to refuse undeclared shaping parameters.** The standard requires it; the package doesn't
  do it. Add a filter provider like the one above, once, in each service.
- **Parameters bound another way.** The filter above reads the handler's own parameters. A shaping
  parameter that arrives inside an `[AsParameters]` model, or that a handler reads from
  `HttpRequest.Query` itself, is the handler's to check.
- **Headers of your own.** A header your service reads and doesn't recognize the value of is yours
  to refuse or ignore. Any other header is ignored.

## See also

- [Filtering](../querying/filtering.md), [Sorting](../querying/sorting.md),
  [Paging](../querying/paging.md) and [Sparse fieldsets](../querying/sparse-fieldsets.md): the
  parameters an operation declares.
- [All-or-nothing and best-effort](../bulk-and-asynchronous-work/bulk-operations/all-or-nothing-and-best-effort.md):
  the `atomic` parameter.
- [Errors](../payloads-and-responses/errors.md): `IProvideErrors` and the error document.
- [Caller context](../beyond-the-standards/caller-context.md): the headers a product service
  refuses.
- The standard:
  [Unrecognized fields, query parameters, and headers](https://contributing.bitwarden.com/architecture/server/apis/unrecognized-fields-query-parameters-and-headers)
