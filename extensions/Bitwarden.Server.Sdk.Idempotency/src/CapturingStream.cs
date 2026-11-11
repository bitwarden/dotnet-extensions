namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Writes through to the response, keeping a copy until it passes the limit.
/// </summary>
internal sealed class CapturingStream(Stream inner, int limit) : Stream
{
    private readonly MemoryStream _copy = new();

    public bool Overflowed { get; private set; }

    public byte[] Captured => _copy.ToArray();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Keep(buffer);
        inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Keep(buffer.Span);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    private void Keep(ReadOnlySpan<byte> buffer)
    {
        if (Overflowed)
        {
            return;
        }

        if (_copy.Length + buffer.Length > limit)
        {
            Overflowed = true;
            _copy.SetLength(0);
            return;
        }

        _copy.Write(buffer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copy.Dispose();
        }

        base.Dispose(disposing);
    }
}
