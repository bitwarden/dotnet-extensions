# Bitwarden.Server.Sdk.Authentication

## About

This package enables the ability to have Bitwarden flavored authentication.

## How to use

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBitwardenAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MyPolicy",
        policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim("my_claim", "my_value")
    );
});

var app = builder.Build();

app.UseRouting();

app.UseBitwardenAuthentication();
app.UseAuthorization();

app.MapGet("/", () =>
{
    return Results.Ok("Hello!");
})
    .RequireAuthorization("MyPolicy");

app.Run();
```

The `UseBitwardenAuthentication()` replaces the need for `UseAuthentication` but it does NOT replace
the need for `UseAuthorization()`.

## Customization

Authentication can be configured via any property in the [`JwtBearerOptions`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.jwtbeareroptions)
class through the `Authentication:Schemes:Bearer` configuration section. The only required setting
is `Authentication:Schemes:Bearer:Authority` which should be a URL of the Bitwarden Identity service
you trust. If you need to change the `MapInboundClaims`,
`TokenValidationParameters.ValidateAudience`, `TokenValidationParameters.ValidTypes`, or
`TokenValidationParameters.NameClaimType` options you can not do so through the previous mentioned
configuration section and must instead do something like:

```csharp
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .PostConfigure(options =>
    {
        options.MapInboundClaims = true;
    });
```

Below is an example of configuring the authority through JSON configuration.

```json
{
  "Authentication": {
    "Schemes": {
      "Bearer": {
        "Authority": "https://identity.bitwarden.com"
      }
    }
  }
}
```
