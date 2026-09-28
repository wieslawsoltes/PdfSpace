using PdfSpace.Core;

namespace PdfSpace.Pdf;
public enum PdfPageObjectKind
{
    Image,
    Path,
    Text,
    Form,
    Shading
}

public enum PdfObjectAlignment
{
    Left,
    Center,
    Right,
    Top,
    Middle,
    Bottom
}

public enum PdfObjectOrder
{
    Back,
    Backward,
    Forward,
    Front
}

/// <summary>A source-validated painted occurrence. Bounds are in unrotated workspace page points.</summary>
public sealed record PdfPageObject(Guid PageId, Guid SourceId, int SourcePage, string SourceHash, string ScopePath, int Start, int End, PdfPageObjectKind Kind, RectD Bounds, PdfAffineMatrix LocalToPage, string Fingerprint, bool Editable, string Limitation)
{
    public PdfObjectPaintState Paint { get; init; } = new();
    public string Text { get; init; } = "";
    public bool IsContainer { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public PdfPathNode[] Nodes { get; init; } = [];
}

/// <summary>Native path command; points use the path's original local coordinate system.</summary>
public sealed record PdfPathNode(string Operator, double[] Values);
/// <summary>Explicit solid-color replacement. Null preserves the original paint/color space.</summary>
public sealed record PdfObjectAppearance(uint? Fill = null, uint? Stroke = null, double? StrokeWidth = null, bool? FillEnabled = null, bool? StrokeEnabled = null, bool? EvenOdd = null, PdfLineCap? LineCap = null, PdfLineJoin? LineJoin = null, double? MiterLimit = null, PdfDashPattern? Dash = null);
public sealed record PdfObjectChange(PdfPageObject Target, PdfAffineMatrix Transform);
