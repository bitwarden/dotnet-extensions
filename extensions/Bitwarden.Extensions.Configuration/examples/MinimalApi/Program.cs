var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration.AddUserSecrets<Program>();

builder.Configuration.AddSecretsManager(
    projectId: Guid.Parse("5aabfa15-d60b-416d-a020-b03301462b86"),
    builder.Configuration.GetValue<string>("SecretsManager:AccessToken")!,
    reloadInterval: TimeSpan.FromMinutes(1));

var app = builder.Build();

app.MapGet("/", (IConfiguration configuration) => ((IConfigurationRoot)configuration).GetDebugView());

app.Run();
