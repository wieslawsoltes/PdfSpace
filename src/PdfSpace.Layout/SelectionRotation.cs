using PdfSpace.Core;

namespace PdfSpace.Layout;

/// <summary>Allocation-free pointer rotation in top-left-origin logical page coordinates.</summary>
public static class SelectionRotation
{
    /// <summary>
    /// Signed shortest angle between pointer-down and current vectors around the selection center.
    /// Positive angles turn clockwise. Passing through the center returns zero; a zero snapStep is free rotation.
    /// </summary>
    public static double DeltaDegrees(PointD center, PointD start, PointD current, double snapStep = 0)
    {
        Validate(center); Validate(start); Validate(current);
        if (!double.IsFinite(snapStep) || snapStep < 0 || snapStep > 180) throw new ArgumentOutOfRangeException(nameof(snapStep));
        var a = start - center; var b = current - center; Validate(a); Validate(b);
        if (Math.Max(Math.Abs(a.X), Math.Abs(a.Y)) < 1e-9 || Math.Max(Math.Abs(b.X), Math.Abs(b.Y)) < 1e-9) return 0;
        var angle = Math.IEEERemainder((Math.Atan2(b.Y, b.X) - Math.Atan2(a.Y, a.X)) * (180 / Math.PI), 360);
        return snapStep == 0 ? angle : Math.Round(angle / snapStep, MidpointRounding.AwayFromZero) * snapStep;
    }

    private static void Validate(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentOutOfRangeException(nameof(point));
    }
}
