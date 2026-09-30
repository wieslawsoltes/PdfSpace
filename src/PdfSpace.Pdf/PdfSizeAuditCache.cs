using PdfSpace.Core;

namespace PdfSpace.Pdf;

/// <summary>Single-thread-affine, bounded source-identity cache. Results and keys never own original PDF buffers.</summary>
public sealed class PdfSizeAuditCache
{
    public const int MaximumWorkspaceObjects = 500_000;
    private sealed record Entry(WeakReference<byte[]> Buffer, PdfSourceSizeReport Report);
    private readonly List<Entry> _entries = [];
    private readonly int _capacity;
    public long SourceParseCount { get; private set; }
    public long CacheHitCount { get; private set; }
    public int CachedSourceCount => _entries.Count;

    public PdfSizeAuditCache(int capacity = 4)
    {
        if (capacity is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public PdfSourceSizeReport Read(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (!entry.Buffer.TryGetTarget(out var source)) { _entries.RemoveAt(i); continue; }
            if (!ReferenceEquals(source, bytes)) continue;
            _entries.RemoveAt(i); _entries.Add(entry); CacheHitCount++;
            return entry.Report;
        }
        var result = PdfSizeAudit.Read(bytes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SourceParseCount++;
        if (_entries.Count == _capacity) _entries.RemoveAt(0);
        _entries.Add(new(new(bytes), result));
        return result;
    }

    public PdfWorkspaceSizeReport Analyze(PdfWorkspace workspace, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorkspaceJson.Validate(workspace);
        var indices = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        var sources = new List<PdfAuditedSource>();
        var counts = new int[7]; var lengths = new long[7]; long fileBytes = 0, streamBytes = 0, objects = 0;
        foreach (var source in workspace.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (indices.TryGetValue(source.Bytes, out var index))
            { sources[index] = sources[index] with { Aliases = sources[index].Aliases + 1 }; continue; }
            var report = Read(source.Bytes, cancellationToken);
            objects += report.IndirectObjectCount;
            if (objects > MaximumWorkspaceObjects) throw new InvalidDataException("Space audit exceeds 500,000 workspace objects.");
            indices.Add(source.Bytes, sources.Count); sources.Add(new(sources.Count + 1, 1, report));
            fileBytes = checked(fileBytes + report.FileBytes); streamBytes = checked(streamBytes + report.EncodedStreamBytes);
            foreach (var category in report.Categories)
            { counts[(int)category.Kind] += category.StreamCount; lengths[(int)category.Kind] += category.EncodedBytes; }
        }
        var categories = Enum.GetValues<PdfStreamKind>().Select(kind => new PdfStreamUsage(kind,
            counts[(int)kind], lengths[(int)kind], streamBytes == 0 ? 0 : lengths[(int)kind] * 100d / streamBytes)).ToArray();
        return new(1, PdfSizeAudit.Scope, workspace.Sources.Length, sources.Count, fileBytes, streamBytes,
            Array.AsReadOnly(categories), sources.AsReadOnly());
    }

    /// <summary>Releases reports and weak keys; telemetry counters remain cumulative.</summary>
    public void Clear() => _entries.Clear();
}
