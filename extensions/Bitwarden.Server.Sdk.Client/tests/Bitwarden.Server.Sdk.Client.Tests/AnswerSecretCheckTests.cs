using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A client reading a service cache refuses to start outside Development without the answer secret,
/// as the host's startup validation runs it.
/// </summary>
public sealed class AnswerSecretCheckTests
{
    private static readonly string Secret = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

    [Fact]
    public void Start_WithACacheButNoSecret_OutsideDevelopment_Refused()
    {
        using var provider = Provider("Production", cache: "redis:6379", secret: null);

        var refusal = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(OwnedAnswerKey.SecretKey, refusal.Message);
    }

    [Theory]
    [InlineData("Development", "redis:6379", false)]
    [InlineData("Production", null, false)]
    [InlineData("Production", "redis:6379", true)]
    public void Start_WhenTheSecretIsNotRequiredOrIsSet_Starts(string environment, string? cache, bool withSecret)
    {
        using var provider = Provider(environment, cache, withSecret ? Secret : null);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider Provider(string environment, string? cache, string? secret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OwnedCacheEntry.ConnectionKey] = cache,
                [OwnedAnswerKey.SecretKey] = secret,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        services.AddLogging();
        services.AddCachedReads<AnswerSecretCheckTests>("callee-service");
        return services.BuildServiceProvider();
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "caller";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
