using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Environment.Setup;
using Bitwarden.Server.Sdk.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

if (builder.Configuration.GetValue("globalSettings:selfHosted", false))
{
    builder.Services.AddInfrastructureRequirement("self-signed-certificate", new JsonObject { ["name"] = "identity" });
}
else
{
    builder.Services.AddInfrastructureRequirement("distributed-cache");
}

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "development-seed" });
}

if (builder.Configuration["Sample:ExtraDatabase"] is { } extraDatabase)
{
    builder.Services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = extraDatabase });
}

builder.Services.AddInfrastructureRequirement("messaging-topic", new JsonObject { ["topic"] = "orders" });
builder.Services.AddInfrastructureRequirement("messaging-subscription", new JsonObject { ["topic"] = "orders", ["subscription"] = "billing" });

builder.Services.AddBitwardenEnvironment();
builder.Services.AddOptions<SelfHostDetails>()
    .Configure<IConfiguration>((details, config) =>
    {
        if (config.GetValue<bool>("globalSettings:selfHosted"))
            details.MakeSelfHost(config["globalSettings:selfHostFlavor"] ?? "unknown");
        else
            details.MakeCloud();
    });

// Decided at collection time, as a library that only sees IServiceCollection would.
builder.Services.AddOptions<InfrastructureOptions>()
    .Configure<IBitwardenEnvironment>((infrastructure, environment) =>
    {
        if (!environment.SelfHosted)
        {
            infrastructure.Add("messaging-topic", new JsonObject { ["topic"] = "audit" });
        }
    });

// Neither of these may run while collecting infrastructure.
builder.Services.AddHostedService<ThrowingHostedService>();
builder.Services.AddOptions<SampleOptions>()
    .Validate(_ => false, "Sample options are never valid.")
    .ValidateOnStart();

var app = builder.Build();

app.Run();

public partial class Program;

internal sealed class SampleOptions;

internal sealed class ThrowingHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("Hosted services must not start while collecting infrastructure.");

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
