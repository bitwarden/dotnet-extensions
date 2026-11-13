using System.Net;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static Bitwarden.Server.Sdk.Client.Tests.PipelineHarness;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// One circuit breaker per callee: calls refused at once while it is down, and a probe to find it back.
/// </summary>
public sealed class CircuitBreakerHandlerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACalleeFailingInARow_IsRefusedAtOnceUntilAProbeFindsItBack()
    {
        var clock = new FakeTimeProvider();
        var callee = new Scripted(Refused, Refused, Refused, Ok);
        await using var services = Services(callee, clock, options =>
        {
            options.Retries = 0;
            options.BreakAfter = 3;
            options.BreakFor = TimeSpan.FromSeconds(5);
        });
        var probe = services.GetRequiredService<Probe>();

        for (var failure = 0; failure < 3; failure++)
        {
            await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
        }

        // Open: refused without asking, as the callee being down would be answered.
        var refused = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
        Assert.Equal(Callee, refused.Service);
        Assert.Contains("failed 3 calls in a row", refused.Detail, StringComparison.Ordinal);
        Assert.Equal(3, callee.Attempts.Count);

        // Half open: one probe, which finds it back and closes the circuit.
        clock.Advance(TimeSpan.FromSeconds(5));
        using (var answered = await probe.SendAsync(HttpMethod.Get, Token))
        {
            Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        }

        using var closed = await probe.SendAsync(HttpMethod.Get, Token);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(5, callee.Attempts.Count);
    }

    [Fact]
    public async Task AProbeThatFails_OpensTheCircuitAgain()
    {
        var clock = new FakeTimeProvider();
        var callee = new Scripted(Refused, Refused, Ok);
        await using var services = Services(callee, clock, options =>
        {
            options.Retries = 0;
            options.BreakAfter = 1;
            options.BreakFor = TimeSpan.FromSeconds(5);
        });
        var probe = services.GetRequiredService<Probe>();

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
        clock.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
        Assert.Equal(2, callee.Attempts.Count);

        clock.Advance(TimeSpan.FromSeconds(5));
        using var back = await probe.SendAsync(HttpMethod.Get, Token);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    // An answer, any answer, is the callee being up: a 500 doesn't open the circuit.
    [Fact]
    public async Task TheCalleesErrors_DoNotOpenTheCircuit()
    {
        var callee = new Scripted(ErrorDocument(HttpStatusCode.InternalServerError));
        await using var services = Services(callee, configure: options => options.BreakAfter = 2);
        var probe = services.GetRequiredService<Probe>();

        for (var call = 0; call < 4; call++)
        {
            await Assert.ThrowsAsync<ServiceCallException>(() => probe.SendAsync(HttpMethod.Get, Token));
        }

        Assert.Equal(4, callee.Attempts.Count);
    }

    // Identity not issuing the call's token says nothing about the callee.
    [Fact]
    public async Task IdentityFailing_DoesNotOpenTheCalleesCircuit()
    {
        var identity = new Switchable();
        var callee = new Scripted(Ok);
        await using var services = Services(callee, configure: options => options.BreakAfter = 2, identity: identity);
        var probe = services.GetRequiredService<Probe>();

        identity.Down = true;
        for (var call = 0; call < 4; call++)
        {
            var unavailable = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
            Assert.Equal(ServiceTokenProvider.Issuer, unavailable.Service);
        }

        identity.Down = false;
        using var response = await probe.SendAsync(HttpMethod.Get, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A callee that doesn't answer within the deadline counts; a caller that gives up doesn't.
    [Fact]
    public async Task TheDeadlinePassing_Counts_TheCallerGivingUp_DoesNot()
    {
        var callee = new Scripted(Hang);
        await using var services = Services(callee, configure: options =>
        {
            options.Retries = 0;
            options.BreakAfter = 2;
            options.Timeout = TimeSpan.FromSeconds(1);
        });
        var probe = services.GetRequiredService<Probe>();

        // The caller gives up as each attempt arrives, so its leaving can't race the deadline.
        for (var call = 0; call < 3; call++)
        {
            using var givingUp = CancellationTokenSource.CreateLinkedTokenSource(Token);
            callee.Arrived = givingUp.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.SendAsync(HttpMethod.Get, givingUp.Token));
        }

        callee.Arrived = null;
        Assert.Equal(3, callee.Attempts.Count);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));
        var refused = await Assert.ThrowsAsync<ServiceUnavailableException>(() => probe.SendAsync(HttpMethod.Get, Token));

        Assert.Contains("in a row", refused.Detail, StringComparison.Ordinal);
        Assert.Equal(5, callee.Attempts.Count);
    }

    // Two clients of one callee share its circuit.
    [Fact]
    public async Task TwoClientsOfOneCallee_ShareItsCircuit()
    {
        var callee = new Scripted(Refused);
        await using var services = Services(callee, configure: options =>
        {
            options.Retries = 0;
            options.BreakAfter = 1;
        });

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => services.GetRequiredService<Probe>().SendAsync(HttpMethod.Get, Token));
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var other = new Probe(factory.CreateClient(typeof(Probe).Name));

        var refused = await Assert.ThrowsAsync<ServiceUnavailableException>(() => other.SendAsync(HttpMethod.Get, Token));

        Assert.Contains("in a row", refused.Detail, StringComparison.Ordinal);
        Assert.Single(callee.Attempts);
    }
}
