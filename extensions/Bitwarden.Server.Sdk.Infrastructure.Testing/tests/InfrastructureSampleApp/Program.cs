using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Environment.Setup;
using Bitwarden.Server.Sdk.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "development-seed" });
}

var extraDatabase = builder.Configuration["Sample:ExtraDatabase"];
if (!string.IsNullOrEmpty(extraDatabase))
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

// The collector post-configures SelfHostDetails, overriding the configuration-based setup above.
builder.Services.AddOptions<InfrastructureOptions>()
    .Configure<IBitwardenEnvironment>((infrastructure, environment) =>
    {
        if (!environment.SelfHosted)
        {
            infrastructure.Add("distributed-cache");
            infrastructure.Add("messaging-topic", new JsonObject { ["topic"] = "audit" });
            return;
        }

        infrastructure.Add("self-signed-certificate", new JsonObject { ["name"] = "identity" });
        if (environment.SelfHostFlavor == "lite")
        {
            infrastructure.Add("volume", new JsonObject { ["path"] = "/etc/bitwarden" });
        }
    });

var app = builder.Build();

app.Run();

public partial class Program;
