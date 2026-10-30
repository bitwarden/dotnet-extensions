using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Common.Tests;

public sealed class RequestMemoTests
{
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Person = Guid.NewGuid();

    private readonly ServiceProvider _services = new ServiceCollection().AddScoped<RequestMemo>().BuildServiceProvider();

    [Fact]
    public async Task RememberAsync_AskedTwice_ReadsOnce()
    {
        using var scope = _services.CreateScope();
        var memo = scope.ServiceProvider.GetRequiredService<RequestMemo>();
        var reads = 0;

        await memo.RememberAsync("grants", Organization, Person, () => Task.FromResult(++reads));
        var second = await memo.RememberAsync("grants", Organization, Person, () => Task.FromResult(++reads));

        Assert.Equal(1, reads);
        Assert.Equal(1, second);
    }

    [Fact]
    public async Task RememberAsync_AnotherFactOrganizationOrPerson_ReadsAgain()
    {
        var memo = new RequestMemo();

        var mine = await memo.RememberAsync("grants", Organization, Person, () => Task.FromResult("mine"));
        var other = await memo.RememberAsync("grantee", Organization, Person, () => Task.FromResult("other fact"));
        var elsewhere = await memo.RememberAsync("grants", Guid.NewGuid(), Person, () => Task.FromResult("elsewhere"));
        var theirs = await memo.RememberAsync("grants", Organization, Guid.NewGuid(), () => Task.FromResult("theirs"));

        Assert.Equal(["mine", "other fact", "elsewhere", "theirs"], new[] { mine, other, elsewhere, theirs });
    }

    [Fact]
    public async Task RememberAsync_AnotherRequest_ReadsAgain()
    {
        using var first = _services.CreateScope();
        using var second = _services.CreateScope();

        await first.ServiceProvider.GetRequiredService<RequestMemo>()
            .RememberAsync("grants", Organization, Person, () => Task.FromResult("first"));
        var answer = await second.ServiceProvider.GetRequiredService<RequestMemo>()
            .RememberAsync("grants", Organization, Person, () => Task.FromResult("second"));

        Assert.Equal("second", answer);
    }

    [Fact]
    public async Task RememberAsync_SharedWithAScopeOfTheSameRequest_ReadsOnce()
    {
        var request = new RequestMemo();
        var organizationScope = new RequestMemo();
        organizationScope.Share(request);

        await request.RememberAsync("grants", Organization, Person, () => Task.FromResult("first"));
        var answer = await organizationScope.RememberAsync("grants", Organization, Person, () => Task.FromResult("second"));

        Assert.Equal("first", answer);
    }

    [Fact]
    public async Task Forget_ThenAsked_ReadsAgain()
    {
        var memo = new RequestMemo();
        await memo.RememberAsync("grants", Organization, Person, () => Task.FromResult("before"));

        memo.Forget();

        Assert.Equal("after", await memo.RememberAsync("grants", Organization, Person, () => Task.FromResult("after")));
    }
}
