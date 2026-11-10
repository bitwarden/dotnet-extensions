namespace Bitwarden.Server.Sdk.WriteSpans.Abstractions.Tests;

public sealed class ServiceWriteScopeTests
{
    // A call retried, or sent again by an operation's retry loop, carries the same key: one write.
    [Fact]
    public void AWriteSentAgainWithTheSameKey_IsOneWrite()
    {
        var guard = new RecordingGuard();

        using (var operation = ServiceWrites.Begin("operation", declared: null, guard))
        {
            ServiceWrites.Outgoing("billing-service", "key-1");
            ServiceWrites.Outgoing("billing-service", "key-1");

            Assert.False(operation.Spans);
            Assert.Single(operation.Steps);
        }

        Assert.Empty(guard.Told);
    }

    // The guard's refusal stops the write; every later write of the operation is refused too, and an operation that
    // caught it and went on is failed when it returns.
    [Fact]
    public void ARefusal_StopsTheWriteAndEveryLaterOne()
    {
        var guard = new RecordingGuard { Refuse = true };

        using var operation = ServiceWrites.Begin("operation", declared: null, guard);
        ServiceWrites.Outgoing("billing-service", "key-1");

        Assert.Throws<InvalidOperationException>(() => ServiceWrites.Outgoing("identity-service", "key-2"));
        Assert.Throws<InvalidOperationException>(() => ServiceWrites.Saving());
        Assert.Throws<InvalidOperationException>(operation.ThrowIfRefused);
        Assert.Single(guard.Told);
    }

    // A write the operation's other work makes while the guard is still deciding waits for the decision, and is refused
    // with it: it is never recorded as made.
    [Fact]
    public async Task AWriteMadeWhileTheGuardDecides_IsRefusedWithIt()
    {
        var guard = new BlockingRefusingGuard();
        var cancellationToken = TestContext.Current.CancellationToken;

        using var operation = ServiceWrites.Begin("operation", declared: null, guard);
        ServiceWrites.Saving();

        var spanning = Task.Run(() => ServiceWrites.Outgoing("billing-service", "key-1"), cancellationToken);
        Assert.True(guard.Entered.Wait(TimeSpan.FromSeconds(30), cancellationToken));

        var meanwhile = Task.Run(() => ServiceWrites.Outgoing("identity-service", "key-2"), cancellationToken);
        await Task.WhenAny(meanwhile, Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken));
        guard.Release.Set();

        await Assert.ThrowsAsync<InvalidOperationException>(() => spanning);
        await Assert.ThrowsAsync<InvalidOperationException>(() => meanwhile);
        Assert.DoesNotContain("identity-service", operation.Steps);
    }

    // Saves carry no key: each one is a write of its own.
    [Fact]
    public void EachSave_IsAWriteOfItsOwn()
    {
        using var operation = ServiceWrites.Begin("operation", declared: null, guard: null);
        ServiceWrites.Saving();
        ServiceWrites.Saving();

        Assert.Equal([ServiceWrites.OwnDatabase, ServiceWrites.OwnDatabase], operation.Steps);
    }

    [Fact]
    public void WithoutARefusal_ThrowIfRefusedDoesNothing()
    {
        using var operation = ServiceWrites.Begin("operation", declared: null, new RecordingGuard());
        ServiceWrites.Outgoing("billing-service", "key-1");
        ServiceWrites.Saving();

        operation.ThrowIfRefused();
    }

    // Steps is a copy: a write made after it was read does not change what the caller holds.
    [Fact]
    public void Steps_IsACopy()
    {
        using var operation = ServiceWrites.Begin("operation", declared: null, guard: null);
        ServiceWrites.Saving();

        var steps = operation.Steps;
        ServiceWrites.Outgoing("billing-service", "key-1");

        Assert.Equal([ServiceWrites.OwnDatabase], steps);
        Assert.Equal([ServiceWrites.OwnDatabase, "billing-service"], operation.Steps);
    }

    // Disposing an inner operation twice restores the outer one only once, so an operation begun since stays current.
    [Fact]
    public void DisposingTwice_RestoresTheOuterOnlyOnce()
    {
        using var outer = ServiceWrites.Begin("outer", declared: null, guard: null);
        var inner = ServiceWrites.Begin("inner", declared: null, guard: null);
        inner.Dispose();

        using var next = ServiceWrites.Begin("next", declared: null, guard: null);
        inner.Dispose();

        Assert.Same(next, ServiceWrites.Current);
    }
}
