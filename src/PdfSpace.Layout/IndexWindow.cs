namespace PdfSpace.Layout;

/// <summary>
/// Allocation-free, page-aligned window over an indexed collection. It retains only
/// integer geometry, never item instances. The default value is a valid empty window.
/// </summary>
public readonly record struct IndexWindow
{
    private readonly int _capacity;
    public int TotalCount { get; }
    public int Capacity => _capacity == 0 ? 64 : _capacity;
    public int Start { get; }
    public int VisibleCount => Math.Min(Capacity, TotalCount - Start);
    public int PageCount => TotalCount == 0 ? 0 : (TotalCount - 1) / Capacity + 1;
    public int PageNumber => TotalCount == 0 ? 0 : Start / Capacity + 1;
    public bool HasPrevious => Start > 0;
    public bool HasNext => Start < TotalCount - VisibleCount;

    public IndexWindow(int totalCount, int requestedStart = 0, int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedStart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        TotalCount = totalCount;
        _capacity = capacity;
        Start = totalCount == 0 ? 0 : Math.Min(requestedStart, totalCount - 1) / capacity * capacity;
    }

    public IndexWindow Move(int pages)
    {
        var start = Math.Clamp((long)Start + (long)pages * Capacity, 0, Math.Max(0L, (long)TotalCount - 1));
        return new(TotalCount, (int)start, Capacity);
    }

    public IndexWindow Reveal(int index)
    {
        if ((uint)index >= (uint)TotalCount) throw new ArgumentOutOfRangeException(nameof(index));
        return Contains(index) ? this : new(TotalCount, index, Capacity);
    }

    public bool Contains(int index) => index >= Start && index - Start < VisibleCount;

    public bool TryGetIndex(int slot, out int index)
    {
        if ((uint)slot < (uint)VisibleCount) { index = Start + slot; return true; }
        index = -1; return false;
    }
}
