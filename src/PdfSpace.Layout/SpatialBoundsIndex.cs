using PdfSpace.Core;

namespace PdfSpace.Layout;

/// <summary>
/// Immutable bounding-volume hierarchy for ordered page objects. Query results are original
/// input ordinals, preserving paint order even when bounds coincide. Caller-owned result
/// buffers allow allocation-free repeated queries. Rectangles may have zero width/height.
/// </summary>
public sealed class SpatialBoundsIndex
{
    private readonly record struct Entry(RectD Bounds, int Ordinal);
    private struct Node
    {
        public RectD Bounds;
        public int Start, Count, Left, Right, MaximumOrdinal;
    }
    private sealed class CenterComparer(bool horizontal) : IComparer<Entry>
    {
        public int Compare(Entry a, Entry b)
        {
            var result = (horizontal ? a.Bounds.Center.X : a.Bounds.Center.Y)
                .CompareTo(horizontal ? b.Bounds.Center.X : b.Bounds.Center.Y);
            return result != 0 ? result : a.Ordinal.CompareTo(b.Ordinal);
        }
    }
    private static readonly CenterComparer Horizontal = new(true), Vertical = new(false);
    private readonly Entry[] _entries;
    private readonly Node[] _nodes;
    private readonly int _root;
    private int _used;
    public int Count => _entries.Length;
    public static SpatialBoundsIndex Empty { get; } = new(Array.Empty<RectD>());

    public SpatialBoundsIndex(IReadOnlyList<RectD> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.Count > 1000000) throw new ArgumentOutOfRangeException(nameof(bounds));
        _entries = new Entry[bounds.Count];
        for (var i = 0; i < bounds.Count; i++)
        {
            Validate(bounds[i]);
            _entries[i] = new(bounds[i], i);
        }
        _nodes = new Node[Math.Max(0, bounds.Count * 2 - 1)];
        _root = bounds.Count == 0 ? -1 : Build(0, bounds.Count);
    }

    /// <summary>Clears and fills results in ascending paint order; returns the number of
    /// leaf bounds tested. The index never stores or retains the result buffer.</summary>
    public int Query(RectD area, List<int> results, bool fullyContained = false)
    {
        ArgumentNullException.ThrowIfNull(results);
        Validate(area);
        results.Clear();
        var tested = 0;
        if (_root >= 0) Visit(_root, area, results, fullyContained, ref tested);
        results.Sort();
        return tested;
    }

    /// <summary>Returns the last-painted hit or -1. Tolerance matches rectangle inflation,
    /// including boundary and zero-area hits. No per-query managed allocations.</summary>
    public int HitTest(PointD point, double tolerance = 0) => HitTest(point, tolerance, out _);
    public int HitTest(PointD point, double tolerance, out int testedBounds)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(point));
        var area = new RectD(point.X - tolerance, point.Y - tolerance, tolerance * 2, tolerance * 2);
        Validate(area);
        var best = -1;
        testedBounds = 0;
        if (_root >= 0) Hit(_root, area, ref best, ref testedBounds);
        return best;
    }

    private int Build(int start, int count)
    {
        var at = _used++;
        var bounds = _entries[start].Bounds;
        var maximum = _entries[start].Ordinal;
        for (var i = start + 1; i < start + count; i++)
        {
            bounds = RectD.Union(bounds, _entries[i].Bounds);
            maximum = Math.Max(maximum, _entries[i].Ordinal);
        }
        Validate(bounds);
        _nodes[at] = new() { Bounds = bounds, Start = start, Count = count, MaximumOrdinal = maximum, Left = -1, Right = -1 };
        if (count <= 8) return at;
        Array.Sort(_entries, start, count, bounds.Width >= bounds.Height ? Horizontal : Vertical);
        var left = Build(start, count / 2);
        var right = Build(start + count / 2, count - count / 2);
        _nodes[at].Left = left;
        _nodes[at].Right = right;
        return at;
    }
    private void Visit(int id, RectD area, List<int> results, bool contained, ref int tested)
    {
        ref readonly var node = ref _nodes[id];
        if (!node.Bounds.Intersects(area)) return;
        if (node.Left >= 0)
        {
            Visit(node.Left, area, results, contained, ref tested);
            Visit(node.Right, area, results, contained, ref tested);
            return;
        }
        for (var i = node.Start; i < node.Start + node.Count; i++)
        {
            var entry = _entries[i];
            tested++;
            if (contained ? Contains(area, entry.Bounds) : area.Intersects(entry.Bounds)) results.Add(entry.Ordinal);
        }
    }
    private void Hit(int id, RectD area, ref int best, ref int tested)
    {
        ref readonly var node = ref _nodes[id];
        if (node.MaximumOrdinal <= best || !node.Bounds.Intersects(area)) return;
        if (node.Left >= 0)
        {
            var leftFirst = _nodes[node.Left].MaximumOrdinal > _nodes[node.Right].MaximumOrdinal;
            Hit(leftFirst ? node.Left : node.Right, area, ref best, ref tested);
            Hit(leftFirst ? node.Right : node.Left, area, ref best, ref tested);
            return;
        }
        for (var i = node.Start; i < node.Start + node.Count; i++)
        {
            var entry = _entries[i];
            if (entry.Ordinal <= best) continue;
            tested++;
            if (entry.Bounds.Intersects(area)) best = entry.Ordinal;
        }
    }
    private static bool Contains(RectD a, RectD b) => a.X <= b.X && a.Y <= b.Y && a.Right >= b.Right && a.Bottom >= b.Bottom;
    private static void Validate(RectD r)
    {
        if (!r.IsFinite || r.Width < 0 || r.Height < 0 || !double.IsFinite(r.Right) || !double.IsFinite(r.Bottom) ||
            !double.IsFinite(r.Center.X) || !double.IsFinite(r.Center.Y))
            throw new ArgumentOutOfRangeException(nameof(r), "Finite, nonnegative rectangle dimensions are required.");
    }
}
