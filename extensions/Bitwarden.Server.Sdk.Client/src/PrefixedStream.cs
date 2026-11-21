namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What was already read of a body, then the rest of it.
/// </summary>
internal sealed class PrefixedStream(byte[] prefix, Stream rest) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position < prefix.Length)
        {
            var taken = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsSpan(_position, taken).CopyTo(buffer);
            _position += taken;
            return taken;
        }

        return rest.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var taken = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, taken).CopyTo(buffer);
            _position += taken;
            return ValueTask.FromResult(taken);
        }

        return rest.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            rest.Dispose();
        }

        base.Dispose(disposing);
    }
}
