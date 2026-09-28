using System.Collections.ObjectModel;

namespace PdfSpace.Pdf;

public enum PdfLineCap { Butt, Round, Square }
public enum PdfLineJoin { Miter, Round, Bevel }
public enum PdfBlendMode
{
    Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn,
    HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity
}

/// <summary>Immutable native dash lengths and phase in the object's local user space.</summary>
public sealed class PdfDashPattern
{
    public static PdfDashPattern Solid { get; } = new([], 0);
    public ReadOnlyCollection<double> Lengths { get; }
    public double Phase { get; }
    public PdfDashPattern(IEnumerable<double> lengths, double phase = 0)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        var copy = lengths.Take(65).ToArray();
        if (copy.Length > 64 || copy.Any(v => !double.IsFinite(v) || v is < 0 or > 10000) ||
            copy.Length > 0 && copy.All(v => v == 0) || !double.IsFinite(phase) || phase is < 0 or > 1000000)
            throw new ArgumentException("Use at most 64 finite dash lengths from 0 to 10,000, not all zero, and a phase from 0 to 1,000,000.");
        Lengths = Array.AsReadOnly(copy);
        Phase = phase;
    }
    internal static bool Same(PdfDashPattern? a, PdfDashPattern? b) => ReferenceEquals(a, b) ||
        a is not null && b is not null && a.Phase == b.Phase && a.Lengths.SequenceEqual(b.Lengths);
}

/// <summary>Null leaves the native parameter unchanged. Opacity is a per-paint alpha override,
/// not group opacity; inherited image/graphics soft masks remain effective.</summary>
public sealed record PdfObjectCompositing(double? FillOpacity = null, double? StrokeOpacity = null, PdfBlendMode? BlendMode = null);

/// <summary>Effective state at painting operators. Null indicates mixed or unrecognized input,
/// not a default. A text object can contain several different painting states.</summary>
public sealed record PdfObjectPaintState
{
    public double? StrokeWidth { get; init; } = 1;
    public PdfLineCap? LineCap { get; init; } = PdfLineCap.Butt;
    public PdfLineJoin? LineJoin { get; init; } = PdfLineJoin.Miter;
    public double? MiterLimit { get; init; } = 10;
    public PdfDashPattern? Dash { get; init; } = PdfDashPattern.Solid;
    public double? FillOpacity { get; init; } = 1;
    public double? StrokeOpacity { get; init; } = 1;
    public PdfBlendMode? BlendMode { get; init; } = PdfBlendMode.Normal;
    public bool? AlphaIsShape { get; init; } = false;
    public bool? HasSoftMask { get; init; } = false;
    internal static PdfObjectPaintState Merge(PdfObjectPaintState a, PdfObjectPaintState b) => a with
    {
        StrokeWidth = a.StrokeWidth == b.StrokeWidth ? a.StrokeWidth : null,
        LineCap = a.LineCap == b.LineCap ? a.LineCap : null,
        LineJoin = a.LineJoin == b.LineJoin ? a.LineJoin : null,
        MiterLimit = a.MiterLimit == b.MiterLimit ? a.MiterLimit : null,
        Dash = PdfDashPattern.Same(a.Dash, b.Dash) ? a.Dash : null,
        FillOpacity = a.FillOpacity == b.FillOpacity ? a.FillOpacity : null,
        StrokeOpacity = a.StrokeOpacity == b.StrokeOpacity ? a.StrokeOpacity : null,
        BlendMode = a.BlendMode == b.BlendMode ? a.BlendMode : null,
        AlphaIsShape = a.AlphaIsShape == b.AlphaIsShape ? a.AlphaIsShape : null,
        HasSoftMask = a.HasSoftMask == b.HasSoftMask ? a.HasSoftMask : null
    };
}
