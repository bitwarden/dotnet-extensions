var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddSecretsManager(
    Guid.Parse("5aabfa15-d60b-416d-a020-b03301462b86"),
    builder.Configuration.GetValue<string>("SecretsManager:AccessToken")!,
    TimeSpan.FromMinutes(1));

// Add services to the container.

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/config", (IConfiguration configuration) => ((IConfigurationRoot)configuration).GetDebugView());

app.Run();
