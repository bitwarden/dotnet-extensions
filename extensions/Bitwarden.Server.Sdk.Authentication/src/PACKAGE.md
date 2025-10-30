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

var app = app.Build();

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
