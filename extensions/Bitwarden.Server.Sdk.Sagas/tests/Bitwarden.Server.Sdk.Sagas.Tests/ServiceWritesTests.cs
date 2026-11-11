namespace Bitwarden.Server.Sdk.Sagas.Tests;

public sealed class ServiceWritesTests
{
    private static readonly SpansServicesAttribute Declared = new(SpanShape.Compensated, "Each write has an undo, run newest first when a later one fails.");

    [Fact]
    public void OutsideAnOperation_NothingIsCounted()
    {
        Assert.Null(ServiceWrites.Current);

        ServiceWrites.Outgoing("billing-service", "key-1");
        ServiceWrites.Saving();

        Assert.Null(ServiceWrites.Current);
    }

    // Two writes, one of them to another service: told once, before the second, with where each went.
    [Fact]
    public void ASaveThenAWriteToAnotherService_Spans()
    {
        var guard = new RecordingGuard();

        using (var operation = ServiceWrites.Begin("POST /api/v1/things", declared: null, guard))
        {
            ServiceWrites.Saving();
            Assert.False(operation.Spans);

            ServiceWrites.Outgoing("billing-service", "key-1");
            ServiceWrites.Outgoing("identity-service", "key-2");

            Assert.True(operation.Spans);
            Assert.Equal([ServiceWrites.OwnDatabase, "billing-service", "identity-service"], operation.Steps);
        }

        var told = Assert.Single(guard.Told);
        Assert.Equal("POST /api/v1/things", told.Operation);
        Assert.Equal([ServiceWrites.OwnDatabase, "billing-service"], told.Steps);
        Assert.Null(told.Declared);
    }

    // Two writes to the same other service span too: a failure between them leaves that service half done.
    [Fact]
    public void TwoWritesToOtherServices_Span()
    {
        var guard = new RecordingGuard();

        using (ServiceWrites.Begin("operation", Declared, guard))
        {
            ServiceWrites.Outgoing("billing-service", "key-1");
            ServiceWrites.Outgoing("billing-service", "key-2");
        }

        Assert.Same(Declared, Assert.Single(guard.Told).Declared);
    }

    // Saves alone are the service's own database's business: no other service to leave half done.
    [Fact]
    public void SavesAlone_DoNotSpan()
    {
        var guard = new RecordingGuard();

        using (var operation = ServiceWrites.Begin("operation", declared: null, guard))
        {
            ServiceWrites.Saving();
            ServiceWrites.Saving();

            Assert.False(operation.Spans);
        }

        Assert.Empty(guard.Told);
    }

    // An operation begun inside another counts its own writes, and the outer one is current again after it.
    [Fact]
    public void AnOperationInsideAnother_CountsItsOwnAndRestoresTheOuter()
    {
        using var outer = ServiceWrites.Begin("outer", declared: null, guard: null);
        ServiceWrites.Outgoing("billing-service", "key-1");

        using (var inner = ServiceWrites.Begin("inner", declared: null, guard: null))
        {
            Assert.Same(inner, ServiceWrites.Current);
            ServiceWrites.Saving();
            Assert.Single(inner.Steps);
        }

        Assert.Same(outer, ServiceWrites.Current);
        Assert.Single(outer.Steps);
    }

    // Work the operation left running after it ended (fire and forget) is not counted against it.
    [Fact]
    public async Task WorkLeftRunningAfterTheOperationEnds_IsNotCounted()
    {
        var release = new TaskCompletionSource();
        Task leftRunning;
        ServiceWriteScope operation;

        using (operation = ServiceWrites.Begin("operation", declared: null, guard: null))
        {
            leftRunning = Task.Run(async () =>
            {
                await release.Task;
                ServiceWrites.Outgoing("billing-service", "key-1");
            }, TestContext.Current.CancellationToken);
        }

        release.SetResult();
        await leftRunning;

        Assert.Empty(operation.Steps);
    }
}
