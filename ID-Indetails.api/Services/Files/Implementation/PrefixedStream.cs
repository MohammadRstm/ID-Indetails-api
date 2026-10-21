namespace ID_Indetails.api.Services.Files.Implementation;

/// <summary>
/// An upload read as it arrives, with its first bytes put back in front: they were read to tell what the file is, and
/// the upload can't be rewound. Read-only and forward-only.
/// </summary>
public sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream rest) : Stream
{
    private ReadOnlyMemory<byte> _prefix = prefix;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_prefix.IsEmpty)
        {
            return rest.Read(buffer);
        }

        var taken = Math.Min(buffer.Length, _prefix.Length);
        _prefix.Span[..taken].CopyTo(buffer);
        _prefix = _prefix[taken..];
        return taken;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_prefix.IsEmpty)
        {
            return await rest.ReadAsync(buffer, cancellationToken);
        }

        var taken = Math.Min(buffer.Length, _prefix.Length);
        _prefix[..taken].CopyTo(buffer);
        _prefix = _prefix[taken..];
        return taken;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
