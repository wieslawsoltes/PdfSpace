namespace PdfSpace.Pdf;

/// <summary>Single-thread-affine metadata cache keyed by immutable source identity. Never caches payload bytes or native documents.</summary>
public sealed class PdfEmbeddedFileCache
{
    private sealed record Entry(WeakReference<byte[]> Source, IReadOnlyList<PdfEmbeddedFileInfo> Files);
    private readonly List<Entry> _entries = [];
    private readonly int _capacity;
    public long SourceParseCount { get; private set; }
    public long CacheHitCount { get; private set; }
    public int Count => _entries.Count;
    public PdfEmbeddedFileCache(int capacity = 4)
    {
        if (capacity is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }
    public IReadOnlyList<PdfEmbeddedFileInfo> Read(byte[] source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (!entry.Source.TryGetTarget(out var candidate)) { _entries.RemoveAt(i); continue; }
            if (!ReferenceEquals(candidate, source)) continue;
            _entries.RemoveAt(i); _entries.Add(entry); CacheHitCount++;
            return entry.Files;
        }
        var files = PdfEmbeddedFiles.Read(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (_entries.Count == _capacity) _entries.RemoveAt(0);
        _entries.Add(new(new(source), files)); SourceParseCount++;
        return files;
    }
    public void Clear() => _entries.Clear();
}
