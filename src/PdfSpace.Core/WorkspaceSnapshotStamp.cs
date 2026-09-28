namespace PdfSpace.Core;

/// <summary>
/// Remembers a workspace identity without retaining its original PDF buffers.
/// Use this for snapshot-aware UI caches whose values do not own the document.
/// The immutable current workspace or an active transaction must own its lifetime.
/// </summary>
public sealed class WorkspaceSnapshotStamp
{
    private WeakReference<PdfWorkspace>? _snapshot;

    public bool Matches(PdfWorkspace snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _snapshot is not null && _snapshot.TryGetTarget(out var remembered) &&
            ReferenceEquals(remembered, snapshot);
    }

    public void Remember(PdfWorkspace snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_snapshot is null) _snapshot = new(snapshot);
        else _snapshot.SetTarget(snapshot);
    }

    public void Clear() => _snapshot = null;
}
