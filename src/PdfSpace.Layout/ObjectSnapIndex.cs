using PdfSpace.Core;

namespace PdfSpace.Layout;

/// <summary>A page-space alignment line spanning the moving bounds and its target.</summary>
public readonly record struct ObjectSnapGuide(double Coordinate, double Start, double End);

/// <summary>The translation correction and optional vertical/horizontal alignment guides.</summary>
public readonly record struct ObjectSnapResult(RectD Bounds, PointD Correction,
    ObjectSnapGuide? VerticalGuide, ObjectSnapGuide? HorizontalGuide);

/// <summary>
/// Immutable sorted edge/center index. Construction is O(n log n); each allocation-free
/// query examines at most twelve candidates using binary search. Entries must exclude
/// the moving selection. Put the visible page box first to give it deterministic tie priority.
/// </summary>
public sealed class ObjectSnapIndex
{
    private readonly record struct Anchor(double Coordinate, int Target);
    private readonly record struct Match(double Correction, int Target, int MovingAnchor);
    private readonly RectD[] _targets;
    private readonly Anchor[] _x, _y;
    public int Count => _targets.Length;

    public ObjectSnapIndex(IReadOnlyList<RectD> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count > 20001) throw new ArgumentOutOfRangeException(nameof(targets));
        _targets = new RectD[targets.Count];
        _x = new Anchor[checked(targets.Count * 3)];
        _y = new Anchor[_x.Length];
        for (var i = 0; i < targets.Count; i++)
        {
            var bounds = targets[i]; Validate(bounds); _targets[i] = bounds;
            _x[i * 3] = new(bounds.X, i); _x[i * 3 + 1] = new(bounds.Center.X, i); _x[i * 3 + 2] = new(bounds.Right, i);
            _y[i * 3] = new(bounds.Y, i); _y[i * 3 + 1] = new(bounds.Center.Y, i); _y[i * 3 + 2] = new(bounds.Bottom, i);
        }
        static int Compare(Anchor a, Anchor b)
        {
            var order = a.Coordinate.CompareTo(b.Coordinate);
            return order == 0 ? a.Target.CompareTo(b.Target) : order;
        }
        Array.Sort(_x, Compare); Array.Sort(_y, Compare);
    }

    /// <summary>
    /// Align nearest edges or centers within tolerance in page units. Axis locks are respected
    /// by disabling the perpendicular axis. Call with screenTolerance / zoom for constant hit slop.
    /// </summary>
    public ObjectSnapResult Snap(RectD moving, double tolerance, bool horizontal = true, bool vertical = true)
    {
        Validate(moving);
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var x = horizontal ? Nearest(_x, moving.X, moving.Width, tolerance) : null;
        var y = vertical ? Nearest(_y, moving.Y, moving.Height, tolerance) : null;
        var delta = new PointD(x?.Correction ?? 0, y?.Correction ?? 0);
        var bounds = moving.Translate(delta); Validate(bounds);
        ObjectSnapGuide? vx = x is { } xm
            ? new(moving.X + moving.Width * (xm.MovingAnchor / 2d) + xm.Correction,
                Math.Min(bounds.Y, _targets[xm.Target].Y), Math.Max(bounds.Bottom, _targets[xm.Target].Bottom)) : null;
        ObjectSnapGuide? hy = y is { } ym
            ? new(moving.Y + moving.Height * (ym.MovingAnchor / 2d) + ym.Correction,
                Math.Min(bounds.X, _targets[ym.Target].X), Math.Max(bounds.Right, _targets[ym.Target].Right)) : null;
        return new(bounds, delta, vx, hy);
    }

    private static Match? Nearest(Anchor[] entries, double start, double length, double tolerance)
    {
        Match? best = null;
        for (var anchor = 0; anchor < 3; anchor++)
        {
            var coordinate = start + length * (anchor / 2d);
            var next = LowerBound(entries, coordinate);
            // Choose the FIRST duplicate coordinate, not an arbitrary sort result.
            var previous = next > 0 ? LowerBound(entries, entries[next - 1].Coordinate) : -1;
            for (var side = 0; side < 2; side++)
            {
                var index = side == 0 ? next : previous;
                if ((uint)index >= entries.Length) continue;
                var candidate = new Match(entries[index].Coordinate - coordinate, entries[index].Target, anchor);
                var distance = Math.Abs(candidate.Correction);
                if (distance > tolerance) continue;
                if (best is not { } current || distance < Math.Abs(current.Correction) ||
                    distance == Math.Abs(current.Correction) && (candidate.Target < current.Target ||
                    (candidate.Target == current.Target && (candidate.MovingAnchor < current.MovingAnchor ||
                    candidate.MovingAnchor == current.MovingAnchor && candidate.Correction < current.Correction)))) best = candidate;
            }
        }
        return best;
    }

    private static int LowerBound(Anchor[] entries, double coordinate)
    {
        var left = 0; var right = entries.Length;
        while (left < right)
        {
            var middle = left + ((right - left) >> 1);
            if (entries[middle].Coordinate < coordinate) left = middle + 1; else right = middle;
        }
        return left;
    }

    private static void Validate(RectD bounds)
    {
        if (!bounds.IsFinite || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) || bounds.Width < 0 || bounds.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Snap bounds must be finite and nonnegative.");
    }
}
