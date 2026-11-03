# Authentication and authorization

Every operation a service maps is authenticated and authorized unless it says otherwise. How a
caller proves its identity and how a service authorizes an operation are the subject of a
[forthcoming standard](https://contributing.bitwarden.com/architecture/server/apis#authentication-and-authorization);
until it exists, this page describes what the package enforces.

## What the package does

- **Closed by default.** A fallback authorization policy holds every endpoint to the service's
  `Authorization` defaults, so a new endpoint is closed until someone opens it. A request without a
  valid token is refused `401`; an authenticated caller who doesn't meet the requirements, `403`.
- **Bearer tokens** are validated by Bitwarden.Server.Sdk.Authentication from
  `Authentication:Schemes:Bearer:*`, as `at+jwt` access tokens.
- **Token audiences are always checked.** A token is accepted only when its `aud` names the service
  (`IServiceIdentity.Audience`, the identifier passed to `Service`). This is not a setting. A token
  naming several audiences is accepted when one is this service's.
- **Three levels of requirements,** most specific winning: the service's `AuthorizationDefaults`, an
  `[Authorization]` on the handler class, and one on the handler.
- **Scopes by kind of operation.** With `ReadScope` and `WriteScope` set, every read demands the
  first and everything else (creates, updates, deletes, actions and their bulk forms) the second.
- **Context requirements.** `Organization`, `User` and `Provider` are each `Required`, `Optional` or
  `Forbidden`. The defaults are organization required, user optional and provider forbidden.
- **Roles and permissions** (`OrganizationRole`, `Permission`, `ProviderRole`), read from a product
  service's session through `ICallerRole`.
- **The startup check** refuses to start a service when a declaration contradicts the operation's
  authorization, for example a handler taking `Person` that doesn't require a user.

## Example

Declare what most operations demand once, on the service:

```csharp
using Bitwarden.Server.Sdk.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;

internal sealed class WidgetService(string[] args) : Service("widget-service", "Widget Service", args)
{
    // Every operation demands one of these scopes by its kind, and accepts a request naming
    // no organization.
    protected override AuthorizationDefaults Authorization => new()
    {
        Organization = ContextRequirement.Optional,
        ReadScope = "widgets.read",
        WriteScope = "widgets.write",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWidgetStore, WidgetStore>();

        // The service also accepts tokens addressed to "widget-legacy" at one surface it hosts.
        // The platform keeps "widget-service" beside it.
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            options.TokenValidationParameters.ValidAudiences = ["widget-legacy"]);
    }

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        WidgetsEndpoints.Map(endpoints);
}
```

Then say on an operation only where it differs. `Person` is the acting user as a parameter, and
`IContext` (Bitwarden.Server.Sdk.Common) says whom the request concerns:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;
using Bitwarden.Server.Sdk.Service.Resources;
using static Bitwarden.Server.Sdk.Service.StandardResponses;

[ApiTag(Description = "Widgets, which a person keeps.")]
public static class WidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var widgets = endpoints.MapGroup("api/v1/widgets").WithStandards();
        widgets.MapGet("current", GetMyWidget);
        widgets.MapGet("{id}", GetWidget);
        widgets.MapDelete("{id}", DeleteSharedWidget);
    }

    /// <summary>
    /// Get the person's own widget
    /// </summary>
    [Authorization(User = ContextRequirement.Required)]
    [StandardReadOne("current")]
    public static async Task<ResourceDocument<Widget>> GetMyWidget(
        Person person, IWidgetStore store, CancellationToken cancellationToken) =>
        ReadOneResponse(await store.FindForAsync(person.Id, cancellationToken)
            ?? throw new NotFoundException("widget", "current"));

    /// <summary>
    /// Get a widget
    /// </summary>
    [StandardReadOne]
    public static async Task<ResourceDocument<Widget>> GetWidget(
        string id, IContext context, IWidgetStore store, CancellationToken cancellationToken) =>
        // IContext says whom the request concerns: here, the organization it names, if any.
        ReadOneResponse(await store.FindAsync(id, context.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("widget", id));

    /// <summary>
    /// Delete a shared widget
    /// </summary>
    [Authorization(Scope = "widgets.admin", Organization = ContextRequirement.Required)]
    [StandardDelete]
    public static Task DeleteSharedWidget(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);
}
```

A caller without a token, and one missing the scope, see:

```http
HTTP/1.1 401 Unauthorized
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"401","code":"unauthenticated","title":"The caller is not authenticated.","detail":"A valid access token is required."}]}
```

```http
HTTP/1.1 403 Forbidden
Content-Type: application/json; charset=utf-8

{"errors":[{"status":"403","code":"forbidden","title":"The caller is not permitted to do this.","detail":"The 'widgets.admin' scope is required."}]}
```

A missing or unwanted context is refused `403` too, naming what is wrong: in a domain service "The
'Bitwarden-Organization' header is required.", in a product service "The session must name an
organization."

### Roles, permissions and `IAuthorizesOperation` (product services)

Roles and permissions come from a person's session, so they apply in a product service. A role is
satisfied by it or any role above it (`User`, `Custom`, `Admin`, `Owner`), and an owner or
administrator holds every permission. A role counts only in the organization the session names.

For a check `[Authorization]` can't express, write an attribute implementing `IAuthorizesOperation`.
It runs after authorization and before anything is bound or validated, on a handler or its class.
Refuse by throwing `ForbiddenException`:

```csharp
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service;

[ProductService]
internal sealed class WidgetPortal(string[] args) : Service("widget-portal", "Widget Portal", args)
{
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IWidgetStore, WidgetStore>();

    protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        SharedWidgetsEndpoints.Map(endpoints);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresWidgetManagerAttribute : Attribute, IAuthorizesOperation
{
    public ValueTask AuthorizeAsync(HttpContext context)
    {
        var caller = context.RequestServices.GetRequiredService<ICallerRole>();

        return caller.Role >= OrganizationRole.Admin || caller.Holds(OrganizationPermission.EditAnyCollection)
            ? ValueTask.CompletedTask
            : throw new ForbiddenException("Only a widget manager may do this.");
    }
}

[ApiTag(Description = "Shared widgets, which an organization keeps.")]
[Authorization(Organization = ContextRequirement.Required, Scope = "widgets.write")]
public static class SharedWidgetsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var shared = endpoints.MapGroup("api/v1/shared-widgets").WithStandards();
        shared.MapPost("{id}/actions/archive", Archive);
        shared.MapDelete("{id}", DeleteSharedWidget);
    }

    /// <summary>
    /// Archive a shared widget
    /// </summary>
    [RequiresWidgetManager]
    [StandardAction]
    public static Task Archive(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    /// <summary>
    /// Delete a shared widget
    /// </summary>
    [Authorization(OrganizationRole = OrganizationRole.Admin)]
    [StandardDelete]
    public static Task DeleteSharedWidget(string id, IWidgetStore store, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);
}
```

### Tokens from another issuer

`DiscoveredIssuerTokens` verifies an access token another OpenID Connect issuer signed: a key its
discovery document publishes, the issuer the document names, the lifetime with five minutes' skew,
and the type `at+jwt`. It does not check the audience; check it yourself if the issuer sets one.

```csharp
using Bitwarden.Server.Sdk.Service;

// Keep one instance per issuer: it reads the discovery document and keys once.
var issuer = new DiscoveredIssuerTokens(
    new Uri("https://identity.example.com"), clients.CreateClient("issuer"));

var result = await issuer.ValidateAsync(token, cancellationToken);
var subject = result.IsValid ? result.ClaimsIdentity.FindFirst("sub")?.Value : null;
```

### Other declarations

These go on one handler each, and the service refuses to start when one contradicts the operation's
authorization:

- **`Anonymous = true`** opens an operation to callers with no token. Nothing that touches a
  caller's data should set it.
- **`[AnyScope("a.read", "a.invitations")]`** admits a caller holding any one of several scopes. The
  operation then decides what each scope may ask, with `CallerScopes.Holds(principal, scope)`.
- **`[AcceptsOrganizationCredential(scope)]`** and **`[AcceptsSendCredential]`** (product services)
  accept a token issued to an organization, or minted for one Send's link.
- **`[NamesOrganization]`**, **`[NamesProvider]`** and **`[CreatesOrganization]`** (product
  services) mark operations about an organization or provider the session doesn't name.
- **`[AcceptsAssertedContext(reason, callers)]`** (domain services): see
  [Caller context](caller-context.md).

## Configuration

| Setting                                   | Default | What it does                                                                                                                                   |
| ----------------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `Authentication:Schemes:Bearer:Authority` | None    | The Identity service whose tokens are trusted. Read by Bitwarden.Server.Sdk.Authentication.                                                    |
| `Authentication:Schemes:Bearer:*`         | None    | Any other `JwtBearerOptions` property.                                                                                                         |
| `Tls:RequireHttps`                        | `false` | When true, the bearer authority must be `https://`, or the service does not start.                                                             |
| `Service:PublicIdentityUrl`               | None    | Where a browser reaches Identity, for the documentation UI's token request. Must be absolute `http` or `https`, or the service does not start. |

## What you decide

- **Scopes.** Set `ReadScope` and `WriteScope`. Without them an operation that declares no scope
  demands none, a gap nobody sees.
- **Extra audiences.** A service accepting another audience is responsible for keeping it to the one
  surface that needs it: the package adds it for every endpoint.
- **Row ownership.** Authorization says whether a caller may invoke an operation, not which rows it
  reaches. Filter your queries by `IContext` (or replace `IRowOwnership`, from
  Bitwarden.Server.Sdk.Common).
- **Roles in a domain service.** `ICallerRole` answers `None` there, so an operation demanding a
  role refuses every call. The package does not refuse such a declaration at startup; keep roles on
  product services.
- **Zero knowledge.** A token carries identifiers and scopes, never a key. Authorization never needs
  to read Vault Data, which reaches the service only as Protected Data.

## See also

- [Caller context](caller-context.md)
- [Defining a service](defining-a-service.md)
- [Outbound HTTP](outbound-http.md)
- [API standards: Authentication and authorization](https://contributing.bitwarden.com/architecture/server/apis#authentication-and-authorization)
