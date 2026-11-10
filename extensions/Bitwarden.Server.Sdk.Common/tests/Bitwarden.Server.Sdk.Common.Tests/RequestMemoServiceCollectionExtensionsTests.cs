using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class RequestMemoServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRequestMemo_RegistersOneMemoPerScope()
    {
        using var services = new ServiceCollection().AddRequestMemo().BuildServiceProvider();
        using var first = services.CreateScope();
        using var second = services.CreateScope();

        var memo = first.ServiceProvider.GetRequiredService<RequestMemo>();

        Assert.Same(memo, first.ServiceProvider.GetRequiredService<RequestMemo>());
        Assert.NotSame(memo, second.ServiceProvider.GetRequiredService<RequestMemo>());
    }

    [Fact]
    public void AddRequestMemo_CalledTwice_RegistersOnce()
    {
        var services = new ServiceCollection().AddRequestMemo().AddRequestMemo();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RequestMemo));
    }

    [Fact]
    public void AddRequestMemo_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RequestMemoServiceCollectionExtensions.AddRequestMemo(null!));
    }
}
