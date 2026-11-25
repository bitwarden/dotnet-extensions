namespace Bitwarden.Server.Sdk.Idempotency.Tests;

/// <summary>
/// The response stream a recorded write runs against: everything reaches the caller, and a copy is kept until the
/// answer passes the limit, when the copy is dropped for good.
/// </summary>
public sealed class CapturingStreamTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_ReachTheCallerAndAreKept()
    {
        using var inner = new MemoryStream();
        using var capture = new CapturingStream(inner, limit: 8);

        capture.Write([1, 2, 3], 0, 3);
        capture.Write([4, 5]);
        await capture.WriteAsync(new byte[] { 6, 7 }, 0, 2, Token);
        await capture.WriteAsync(new byte[] { 8 }.AsMemory(), Token);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], inner.ToArray());
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], capture.Captured);
        Assert.False(capture.Overflowed);
    }

    // An answer exactly at the limit is kept; one byte more and nothing is.
    [Fact]
    public void PastTheLimit_OverflowsAndKeepsNothing_ButTheCallerGetsEverything()
    {
        using var inner = new MemoryStream();
        using var capture = new CapturingStream(inner, limit: 4);

        capture.Write([1, 2, 3, 4]);
        Assert.False(capture.Overflowed);

        capture.Write([5]);
        capture.Write([6]);

        Assert.True(capture.Overflowed);
        Assert.Empty(capture.Captured);
        Assert.Equal([1, 2, 3, 4, 5, 6], inner.ToArray());
    }

    // Once over, a later write small enough to fit never restarts the copy, which would keep a truncated answer.
    [Fact]
    public async Task AfterOverflowing_LaterWritesAreNeverKept()
    {
        using var inner = new MemoryStream();
        using var capture = new CapturingStream(inner, limit: 2);

        await capture.WriteAsync(new byte[] { 1, 2, 3 }.AsMemory(), Token);
        await capture.WriteAsync(new byte[] { 4 }.AsMemory(), Token);

        Assert.True(capture.Overflowed);
        Assert.Empty(capture.Captured);
    }

    [Fact]
    public void Flush_ReachesTheCaller()
    {
        var inner = new FlushCounting();
        using var capture = new CapturingStream(inner, limit: 1);

        capture.Flush();

        Assert.Equal(1, inner.Flushes);
    }

    [Fact]
    public void ItIsWriteOnly()
    {
        using var capture = new CapturingStream(new MemoryStream(), limit: 1);

        Assert.True(capture.CanWrite);
        Assert.False(capture.CanRead);
        Assert.False(capture.CanSeek);
        Assert.Throws<NotSupportedException>(() => capture.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => capture.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => capture.Length);
    }

    private sealed class FlushCounting : MemoryStream
    {
        public int Flushes { get; private set; }

        public override void Flush() => Flushes++;
    }
}
