using System.Text.Json.Nodes;
using Bitwarden.Extensions.Hosting.Features;

var builder = WebApplication.CreateBuilder(args);

builder.UseBitwardenDefaults();

var app = builder.Build();

app.UseRouting();

app.UseFeatureFlagChecks();

app.MapGet("/", (IConfiguration config) => ((IConfigurationRoot)config).GetDebugView());

app.MapGet("/requires-feature", (IFeatureService featureService) =>
{
    return featureService.GetAll();
})
    .RequireFeature("feature-one");

app.Run();
