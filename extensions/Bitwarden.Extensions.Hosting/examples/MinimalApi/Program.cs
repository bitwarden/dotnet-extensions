var builder = WebApplication.CreateBuilder(args);

builder.UseBitwardenDefaults();

var app = builder.Build();

app.Run();
