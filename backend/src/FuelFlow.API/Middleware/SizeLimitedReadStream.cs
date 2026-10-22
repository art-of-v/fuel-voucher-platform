namespace FuelFlow.Middleware;

/// <summary>
/// A read-only wrapper that counts the bytes it hands out and refuses to hand out more than
/// the ceiling allows.
/// </summary>
/// <remarks>
/// <para>
/// The ceiling has to hold for a body that does not declare its length. A chunked request
/// carries no <c>Content-Length</c>, so nothing can refuse it before it is read - the only way
/// to bound it is to count the bytes as they go past and refuse the moment the count is over
/// the line.
/// </para>
/// <para>
/// The count is of the bytes read from the stream underneath, not of the bytes handed to the
/// caller. That distinction matters because a body may legitimately be read twice: the rate
/// limiter peeks at the first few kilobytes to find the phone number, and the device-signature
/// middleware reads the whole body to verify the signature. Both of them buffer the body
/// first, so the bytes come out of that buffer the second time and never reach this wrapper
/// again - the count stays the size of the body, not a multiple of it.
/// </para>
/// </remarks>
internal sealed class SizeLimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private long _read;

    public SizeLimitedReadStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Count(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Count(read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Counts what was just read and refuses the request if the body is now over the line.
    /// The bytes that crossed it have already been taken from the stream underneath - there is
    /// no way to put them back - so the body is refused rather than truncated. A truncated body
    /// would be read as a shorter, valid one, which is worse than a refusal.
    /// </summary>
    private void Count(int read)
    {
        _read += read;

        if (_read > _limit)
        {
            throw new RequestBodyTooLargeException(_limit);
        }
    }
}
