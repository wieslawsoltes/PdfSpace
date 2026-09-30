using PdfSpace.Storage;

internal static class WorkspaceFileReaderTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        static byte[] Read(Stream stream, int limit, CancellationToken token = default) => WorkspaceFileReader.ReadBoundedAsync(stream, limit, token).GetAwaiter().GetResult();
        using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        check(Read(source, 3).SequenceEqual(new byte[] { 1, 2, 3 }) && source.CanRead, "bounded file reader returns exact-limit content and leaves input open");
        source.Position = 1;
        check(Read(source, 2).SequenceEqual(new byte[] { 2, 3 }), "bounded file reader measures remaining seekable bytes");
        source.Position = 0;
        reject(() => Read(source, 2), "bounded file reader rejects known oversized inputs before copying");
        check(source.Position == 0, "known oversized file is not consumed");
        using var unknown = new UnknownLengthStream(1000000);
        reject(() => Read(unknown, 10), "nonseekable or growing files stop at the byte budget");
        check(unknown.Consumed == 11, "bounded read consumes at most one byte beyond the remaining budget");
        using var exact = new UnknownLengthStream(65536);
        check(Read(exact, 65536).Length == 65536, "nonseekable exact-boundary stream handles multiple blocks");
        using var empty = new UnknownLengthStream(0);
        check(Read(empty, 0).Length == 0, "zero-byte input budget accepts an empty stream");
        using var overflow = new UnknownLengthStream(1);
        reject(() => Read(overflow, 0), "zero-byte budget rejects a nonempty stream");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        reject(() => Read(source, 3, cancelled.Token), "file reads honor precancellation");
        reject(() => Read(source, -1), "negative file input limit is invalid");
        reject(() => Read(source, int.MaxValue), "file input budget cannot exceed 128 MiB");
    }
    private sealed class UnknownLengthStream(int length) : Stream
    {
        public int Consumed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        { var count = Math.Min(buffer.Length, length - Consumed); buffer[..count].Fill(42); Consumed += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Read(buffer.Span)); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
