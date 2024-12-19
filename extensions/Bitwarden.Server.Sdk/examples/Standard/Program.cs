#if BIT_INCLUDE_FEATURES
using Bitwarden.Server.Sdk.Features;
#endif

var builder = WebApplication.CreateBuilder(args);

builder.UseBitwardenSdk();

var app = builder.Build();

#if BIT_INCLUDE_FEATURES
app.MapGet("/features", (IFeatureService featureService) =>
{
    return featureService.GetAll();
});
#endif

app.Run();
