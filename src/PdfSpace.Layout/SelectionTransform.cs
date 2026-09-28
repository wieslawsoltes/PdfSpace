using PdfSpace.Core;

namespace PdfSpace.Layout;

/// <summary>Stateless, allocation-free geometry for native-object drag gestures in logical page coordinates.</summary>
public static class SelectionTransform
{
    /// <summary>Lock to the larger displacement axis; equal displacements choose the horizontal axis.</summary>
    public static PointD ConstrainMove(PointD delta, bool axisLocked)
    {
        Validate(delta);
        return !axisLocked ? delta : Math.Abs(delta.X) >= Math.Abs(delta.Y) ? new(delta.X, 0) : new(0, delta.Y);
    }

    /// <summary>
    /// Resize using displacement from pointer-down, not the pointer's absolute position, so grabbing
    /// within a handle's hit slop does not jump. Handles run clockwise from top-left (0) to left (7).
    /// Aspect-locked side handles grow symmetrically along the perpendicular axis. Crossing the anchor
    /// clamps the extent instead of implicitly reflecting content; explicit flip commands remain separate.
    /// </summary>
    public static RectD Resize(RectD initial, int handle, PointD delta,
        bool preserveAspect = false, bool fromCenter = false, double minimumExtent = .01)
    {
        Validate(initial);
        Validate(delta);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumExtent);
        if (!double.IsFinite(minimumExtent)) throw new ArgumentOutOfRangeException(nameof(minimumExtent));
        var (hx, hy) = handle switch
        {
            0 => (-1, -1), 1 => (0, -1), 2 => (1, -1), 3 => (1, 0),
            4 => (1, 1), 5 => (0, 1), 6 => (-1, 1), 7 => (-1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(handle))
        };
        if (delta == default) return initial;
        var multiplier = fromCenter ? 2d : 1d;
        var width = hx == 0 ? initial.Width : Math.Max(minimumExtent, initial.Width + hx * delta.X * multiplier);
        var height = hy == 0 ? initial.Height : Math.Max(minimumExtent, initial.Height + hy * delta.Y * multiplier);
        // A zero-area source line has no finite aspect ratio. Its unconstrained axis still remains valid.
        if (preserveAspect && initial.Width > 0 && initial.Height > 0)
        {
            var sx = hx == 0 ? 1 : 1 + hx * delta.X * multiplier / initial.Width;
            var sy = hy == 0 ? 1 : 1 + hy * delta.Y * multiplier / initial.Height;
            var scale = hx == 0 ? sy : hy == 0 ? sx : Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
            scale = Math.Max(scale, Math.Max(minimumExtent / initial.Width, minimumExtent / initial.Height));
            width = initial.Width * scale;
            height = initial.Height * scale;
        }
        var x = fromCenter || hx == 0 ? initial.X + (initial.Width - width) / 2 : hx < 0 ? initial.Right - width : initial.X;
        var y = fromCenter || hy == 0 ? initial.Y + (initial.Height - height) / 2 : hy < 0 ? initial.Bottom - height : initial.Y;
        var result = new RectD(x, y, width, height);
        Validate(result);
        return result;
    }

    /// <summary>Create a rectangle/ellipse from a drag, optionally square and/or centered at pointer-down.</summary>
    public static RectD Create(PointD start, PointD current, bool square = false, bool fromCenter = false)
    {
        Validate(start); Validate(current);
        var delta = current - start;
        if (square)
        {
            var side = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            delta = new(Math.CopySign(side, delta.X), Math.CopySign(side, delta.Y));
        }
        var result = RectD.Between(fromCenter ? start - delta : start, start + delta);
        Validate(result);
        return result;
    }

    private static void Validate(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point), "Pointer coordinates must be finite.");
    }

    private static void Validate(RectD rect)
    {
        if (!rect.IsFinite || !double.IsFinite(rect.Right) || !double.IsFinite(rect.Bottom) || rect.Width < 0 || rect.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(rect), "Selection bounds must be finite and nonnegative.");
    }
}
