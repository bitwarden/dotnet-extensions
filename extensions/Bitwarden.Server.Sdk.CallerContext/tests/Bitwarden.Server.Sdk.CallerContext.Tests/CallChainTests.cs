namespace Bitwarden.Server.Sdk.CallerContext.Tests;

/// <summary>
/// The call chain: service names only, bounded, appended by each caller, and the current request's on the execution
/// context.
/// </summary>
public sealed class CallChainTests
{
    [Fact]
    public void Append_NoChain_IsTheCallerAlone()
    {
        Assert.Equal(["identity-service"], CallChain.Append(null, "identity-service"));
    }

    [Fact]
    public void Append_AddsTheCallerLast()
    {
        Assert.Equal(
            ["api-service", "identity-service"],
            CallChain.Append(["api-service"], "identity-service"));
    }

    [Fact]
    public void Append_AtTheBound_DropsTheOldest()
    {
        IReadOnlyList<string> full = [.. Enumerable.Range(0, CallChain.MaximumServices).Select(i => $"service-{i}")];

        var appended = CallChain.Append(full, "last-service");

        Assert.Equal(CallChain.MaximumServices, appended.Count);
        Assert.Equal("service-1", appended[0]);
        Assert.Equal("last-service", appended[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Identity-Service")]
    [InlineData("a0000000-0000-4000-8000-000000000001,x")]
    public void Append_NotAServiceName_Throws(string service)
    {
        Assert.Throws<ArgumentException>(() => CallChain.Append(null, service));
    }

    // A chain with anything but service names in it is no chain, so the caller's name goes out alone.
    [Theory]
    [MemberData(nameof(NotChains))]
    public void Append_NotAChain_IsTheCallerAlone(string[] chain)
    {
        Assert.Equal(["identity-service"], CallChain.Append(chain, "identity-service"));
    }

    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        IReadOnlyList<string> chain = ["api-service", "identity-service", "organization-service"];

        Assert.True(CallChain.TryParse(CallChain.Format(chain), out var parsed));
        Assert.Equal(chain, parsed);
    }

    // Anything but service names is not a chain: an identifier, an address, a free-text value, an empty entry, or too
    // many services. It is read as none, so it never reaches a log, a span or a metric tag.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("identity-service,")]
    [InlineData("identity-service,,billing-service")]
    [InlineData("identity-service, billing-service")]
    [InlineData("b0000000-0000-4000-8000-000000000001")]
    [InlineData("b0000000000040008000000000000001")]
    [InlineData("identity-service,b0000000-0000-4000-8000-000000000001")]
    [InlineData("someone@example.com")]
    [InlineData("Identity-Service")]
    [InlineData("identity--service")]
    [InlineData("-identity")]
    [InlineData("identity-")]
    [InlineData("9service")]
    [InlineData("identity_service")]
    public void TryParse_NotAChain_False(string? value)
    {
        Assert.False(CallChain.TryParse(value, out var chain));
        Assert.Null(chain);
    }

    [Fact]
    public void TryParse_AnOverlongValue_IsRefusedBeforeItIsSplit()
    {
        var value = new string(',', 1_000_000);
        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.False(CallChain.TryParse(value, out _));

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 10_000);
    }

    [Theory]
    [InlineData("user-b0000000000040008000000000000001")]
    [InlineData("user-b0000000-0000-4000-8000-000000000001")]
    [InlineData("b0000000-0000-4000-8000-000000000001-service")]
    [InlineData("a-b0000000000040008000000000000001-service")]
    public void IsServiceName_AnEmbeddedIdentifier_False(string name)
    {
        Assert.False(CallChain.IsServiceName(name));
        Assert.False(CallChain.TryParse(name, out _));
    }

    [Theory]
    [InlineData("organization-service")]
    [InlineData("service-2")]
    [InlineData("cafe-0123456789abcdef0123456789abcde")]
    public void IsServiceName_AServiceName_True(string name)
    {
        Assert.True(CallChain.IsServiceName(name));
    }

    [Fact]
    public void TryParse_TooManyServices_False()
    {
        var value = string.Join(',', Enumerable.Range(0, CallChain.MaximumServices + 1).Select(i => $"service-{i}"));

        Assert.False(CallChain.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_ANameTooLong_False()
    {
        Assert.False(CallChain.TryParse(new string('s', CallChain.MaximumNameLength + 1), out _));
        Assert.True(CallChain.TryParse(new string('s', CallChain.MaximumNameLength), out _));
    }

    [Fact]
    public void Enter_IsCurrentUntilDisposed_ThenWhatWasBefore()
    {
        Assert.Null(CallChain.Current);

        using (CallChain.Enter(["outer-service"]))
        {
            using (CallChain.Enter(["inner-service"]))
            {
                Assert.Equal(["inner-service"], CallChain.Current);
            }

            Assert.Equal(["outer-service"], CallChain.Current);
        }

        Assert.Null(CallChain.Current);
    }

    [Fact]
    public void Enter_DisposedOutOfOrder_DoesNotBringBackAnEndedChain()
    {
        var outer = CallChain.Enter(["outer-service"]);
        var inner = CallChain.Enter(["inner-service"]);

        outer.Dispose();
        inner.Dispose();

        Assert.Null(CallChain.Current);
    }

    [Fact]
    public void Dispose_Twice_RestoresOnlyOnce()
    {
        using var outer = CallChain.Enter(["outer-service"]);
        var inner = CallChain.Enter(["inner-service"]);
        inner.Dispose();

        using (CallChain.Enter(["other-service"]))
        {
            inner.Dispose();
            Assert.Equal(["other-service"], CallChain.Current);
        }

        Assert.Equal(["outer-service"], CallChain.Current);
    }

    // A chain with anything but service names in it is no chain, and nothing reads it as the request's.
    [Theory]
    [MemberData(nameof(NotChains))]
    public void Enter_NotAChain_IsNone(string[] chain)
    {
        using (CallChain.Enter(chain))
        {
            Assert.Null(CallChain.Current);
        }
    }

    // Nothing that is not a chain reaches the header.
    [Theory]
    [MemberData(nameof(NotChains))]
    public void Format_NotAChain_IsEmpty(string[] chain)
    {
        Assert.Equal(string.Empty, CallChain.Format(chain));
    }

    public static TheoryData<string[]> NotChains => new()
    {
        Array.Empty<string>(),
        new[] { "api-service", "free text" },
        new[] { "b0000000-0000-4000-8000-000000000001" },
        new[] { "api-service", "someone@example.com" },
        new[] { "user-b0000000000040008000000000000001" },
        new[] { "api-service,identity-service" },
    };

    [Fact]
    public void Enter_Longer_KeepsTheMostRecentServices()
    {
        IReadOnlyList<string> longer = [.. Enumerable.Range(0, CallChain.MaximumServices + 1).Select(i => $"service-{i}")];

        using (CallChain.Enter(longer))
        {
            Assert.Equal(longer.Skip(1), CallChain.Current);
        }
    }

    // A chain and the service called back from it, one more than a chain holds, is named whole.
    [Fact]
    public void Format_AChainAndTheServiceCalledBack_NamesEveryService()
    {
        IReadOnlyList<string> loop = [.. Enumerable.Range(0, CallChain.MaximumServices).Select(i => $"service-{i}"), "service-0"];

        Assert.Equal(string.Join(',', loop), CallChain.Format(loop));
    }

    [Fact]
    public async Task Enter_FlowsIntoWorkTheRequestAwaits()
    {
        using (CallChain.Enter(["a-service"]))
        {
            var seen = await Task.Run(() => CallChain.Current, TestContext.Current.CancellationToken);

            Assert.Equal(["a-service"], seen);
        }
    }
}
