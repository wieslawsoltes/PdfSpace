using PdfSpace.Core;

namespace PdfSpace.Editing;

/// <summary>Reference-counts immutable source buffers without copying or hashing their contents.</summary>
internal sealed class SourceRetention
{
    private sealed class Registration(byte[][] buffers)
    {
        public byte[][] Buffers { get; } = buffers;
        public int References { get; set; } = 1;
    }
    private readonly Dictionary<PdfWorkspace, Registration> _snapshots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<byte[], int> _buffers = new(ReferenceEqualityComparer.Instance);
    public long Bytes { get; private set; }

    public void Add(PdfWorkspace snapshot)
    {
        if (_snapshots.TryGetValue(snapshot, out var registration)) { registration.References++; return; }
        var unique = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        foreach (var source in snapshot.Sources)
        {
            unique.Add(source.Bytes);
            if (source.PreviewBytes is { } preview) unique.Add(preview);
        }
        _snapshots.Add(snapshot, new Registration(unique.ToArray()));
        foreach (var buffer in unique)
        {
            if (_buffers.TryGetValue(buffer, out var count)) _buffers[buffer] = count + 1;
            else { _buffers.Add(buffer, 1); Bytes += buffer.LongLength; }
        }
    }

    public void Remove(PdfWorkspace snapshot)
    {
        var registration = _snapshots[snapshot];
        if (--registration.References != 0) return;
        _snapshots.Remove(snapshot);
        foreach (var buffer in registration.Buffers)
        {
            var count = _buffers[buffer];
            if (count > 1) _buffers[buffer] = count - 1;
            else { _buffers.Remove(buffer); Bytes -= buffer.LongLength; }
        }
    }
}
