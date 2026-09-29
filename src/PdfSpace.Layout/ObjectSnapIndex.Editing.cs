using PdfSpace.Core;

namespace PdfSpace.Layout;

/// <summary>A snapped page-space point and the alignment lines supporting the result.</summary>
public readonly record struct ObjectPointSnapResult(PointD Position, PointD Correction,
    ObjectSnapGuide? VerticalGuide, ObjectSnapGuide? HorizontalGuide);

public sealed partial class ObjectSnapIndex
{
    /// <summary>Snap one point to indexed edges/centers without testing three identical moving anchors.</summary>
    public ObjectPointSnapResult SnapPoint(PointD point, double tolerance, bool horizontal = true, bool vertical = true)
    {
        Validate(new(point.X, point.Y, 0, 0)); ValidateTolerance(tolerance);
        var x = horizontal ? NearestAt(_x, point.X, tolerance) : null;
        var y = vertical ? NearestAt(_y, point.Y, tolerance) : null;
        var correction = new PointD(x?.Correction ?? 0, y?.Correction ?? 0);
        var position = point + correction;
        var bounds = new RectD(position.X, position.Y, 0, 0); Validate(bounds);
        return new(position, correction, Vertical(x, bounds, position.X), Horizontal(y, bounds, position.Y));
    }

    /// <summary>
    /// Resize with edge snapping while retaining the fixed anchor, optional center and aspect ratio.
    /// Only grabbed edges participate. For a coupled aspect ratio, choose the candidate with the
    /// smallest maximum grabbed-edge correction, then target priority, then X. Every grabbed-edge
    /// correction must fit tolerance; a snap never crosses the anchor or defeats minimumExtent.
    /// Delta is measured from pointer-down, retaining handle hit slop. A zero delta is exact identity.
    /// </summary>
    public ObjectSnapResult SnapResize(RectD initial, int handle, PointD delta, double tolerance,
        bool preserveAspect = false, bool fromCenter = false, double minimumExtent = .01)
    {
        ValidateTolerance(tolerance);
        var raw = SelectionTransform.Resize(initial, handle, delta, preserveAspect, fromCenter, minimumExtent);
        if (delta == default || raw == initial) return new(raw, default, null, null);
        var (hx, hy) = Direction(handle);
        var edge = Grabbed(raw, hx, hy);
        var x = hx != 0 ? NearestAt(_x, edge.X, tolerance) : null;
        var y = hy != 0 ? NearestAt(_y, edge.Y, tolerance) : null;
        var aspect = preserveAspect && initial.Width > 0 && initial.Height > 0;
        var bounds = raw;
        if (!aspect)
        {
            if (x is { } xm && Candidate(initial, raw, hx, hy, edge.X + xm.Correction, true,
                    false, fromCenter, minimumExtent) is { } xb) bounds = xb;
            else x = null;
            if (y is { } ym && Candidate(initial, bounds, hx, hy, edge.Y + ym.Correction, false,
                    false, fromCenter, minimumExtent) is { } yb) bounds = yb;
            else y = null;
        }
        else
        {
            var xb = x is { } xm ? Candidate(initial, raw, hx, hy, edge.X + xm.Correction,
                true, true, fromCenter, minimumExtent) : null;
            var yb = y is { } ym ? Candidate(initial, raw, hx, hy, edge.Y + ym.Correction,
                false, true, fromCenter, minimumExtent) : null;
            var xd = Distance(xb, edge, hx, hy);
            var yd = Distance(yb, edge, hx, hy);
            // Coupled axes are never independently rounded. A rejected candidate leaves
            // the unsnapped, aspect-correct resize intact, rather than distorting content.
            if (xd <= tolerance && (yd > tolerance || xd < yd || xd == yd && x!.Value.Target <= y!.Value.Target))
                bounds = xb!.Value;
            else if (yd <= tolerance) bounds = yb!.Value;
        }
        var finalEdge = Grabbed(bounds, hx, hy);
        // Only display guides still satisfied by the constrained result.
        if (x is { } xx && Math.Abs(finalEdge.X - (edge.X + xx.Correction)) > 1e-7) x = null;
        if (y is { } yy && Math.Abs(finalEdge.Y - (edge.Y + yy.Correction)) > 1e-7) y = null;
        return new(bounds, new(hx == 0 ? 0 : finalEdge.X - edge.X, hy == 0 ? 0 : finalEdge.Y - edge.Y),
            Vertical(x, bounds, finalEdge.X), Horizontal(y, bounds, finalEdge.Y));
    }

    private static (int X, int Y) Direction(int handle) => handle switch
    {
        0 => (-1, -1), 1 => (0, -1), 2 => (1, -1), 3 => (1, 0),
        4 => (1, 1), 5 => (0, 1), 6 => (-1, 1), 7 => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(handle))
    };

    private static PointD Grabbed(RectD bounds, int hx, int hy) => new(
        hx < 0 ? bounds.X : hx > 0 ? bounds.Right : bounds.Center.X,
        hy < 0 ? bounds.Y : hy > 0 ? bounds.Bottom : bounds.Center.Y);

    private static double Distance(RectD? candidate, PointD edge, int hx, int hy)
    {
        if (candidate is not { } bounds) return double.PositiveInfinity;
        var target = Grabbed(bounds, hx, hy);
        return Math.Max(hx == 0 ? 0 : Math.Abs(target.X - edge.X), hy == 0 ? 0 : Math.Abs(target.Y - edge.Y));
    }

    private static RectD? Candidate(RectD initial, RectD raw, int hx, int hy, double coordinate,
        bool xAxis, bool aspect, bool center, double minimum)
    {
        var width = raw.Width; var height = raw.Height;
        if (xAxis)
        {
            var anchor = center ? initial.Center.X : hx < 0 ? initial.Right : initial.X;
            width = hx * (coordinate - anchor) * (center ? 2 : 1);
            if (aspect) height = initial.Height * (width / initial.Width);
        }
        else
        {
            var anchor = center ? initial.Center.Y : hy < 0 ? initial.Bottom : initial.Y;
            height = hy * (coordinate - anchor) * (center ? 2 : 1);
            if (aspect) width = initial.Width * (height / initial.Height);
        }
        if (width < minimum || height < minimum) return null;
        var x = center || hx == 0 ? initial.X + (initial.Width - width) / 2 : hx < 0 ? initial.Right - width : initial.X;
        var y = center || hy == 0 ? initial.Y + (initial.Height - height) / 2 : hy < 0 ? initial.Bottom - height : initial.Y;
        var result = new RectD(x, y, width, height);
        return result.IsFinite && double.IsFinite(result.Right) && double.IsFinite(result.Bottom) ? result : null;
    }

    private ObjectSnapGuide? Vertical(Match? match, RectD bounds, double coordinate) => match is { } m
        ? new(coordinate, Math.Min(bounds.Y, _targets[m.Target].Y), Math.Max(bounds.Bottom, _targets[m.Target].Bottom)) : null;
    private ObjectSnapGuide? Horizontal(Match? match, RectD bounds, double coordinate) => match is { } m
        ? new(coordinate, Math.Min(bounds.X, _targets[m.Target].X), Math.Max(bounds.Right, _targets[m.Target].Right)) : null;
    private static void ValidateTolerance(double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
    }
}
