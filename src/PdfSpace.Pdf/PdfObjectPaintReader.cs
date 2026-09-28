using PdfSharp.Pdf;
using PdfSharp.Pdf.Content.Objects;

namespace PdfSpace.Pdf;

// Incremental immutable state: one update per state operator, not replaying every
// graphics-state prefix for each painted object on a dense page.
internal static class PdfObjectPaintReader
{
    internal static PdfObjectPaintState Apply(PdfObjectPaintState state, COperator op, PdfDictionary resources)
    {
        double? Number(int i) => i < op.Operands.Count ? op.Operands[i] switch
        { CInteger n => n.Value, CReal n when double.IsFinite(n.Value) => n.Value, _ => null } : null;
        return op.Name switch
        {
            "w" => state with { StrokeWidth = Nonnegative(Number(0)) },
            "J" => state with { LineCap = Cap(Number(0)) },
            "j" => state with { LineJoin = Join(Number(0)) },
            "M" => state with { MiterLimit = Number(0) is >= 1 ? Number(0) : null },
            "d" => state with { Dash = op.Operands.Count == 2 && op.Operands[0] is CArray a ? Dash(a.Select(PdfObjectScanner.N), Number(1)) : null },
            "gs" when op.Operands.FirstOrDefault() is CName name => Extended(state, PdfObjects.Dictionary(PdfObjects.Dictionary(resources.Elements["/ExtGState"])?.Elements[name.Name])),
            _ => state
        };
    }

    private static PdfObjectPaintState Extended(PdfObjectPaintState s, PdfDictionary? gs)
    {
        if (gs is null) return s with { StrokeWidth = null, LineCap = null, LineJoin = null, MiterLimit = null, Dash = null, FillOpacity = null, StrokeOpacity = null, BlendMode = null, AlphaIsShape = null, HasSoftMask = null };
        bool Has(string key) => gs.Elements.ContainsKey(key);
        double? Number(string key) => PdfObjects.Resolve(gs.Elements[key]) switch
        { PdfInteger n => n.Value, PdfReal n when double.IsFinite(n.Value) => n.Value, _ => null };
        var d = PdfObjects.Array(gs.Elements["/D"]);
        return s with
        {
            StrokeWidth = Has("/LW") ? Nonnegative(Number("/LW")) : s.StrokeWidth,
            LineCap = Has("/LC") ? Cap(Number("/LC")) : s.LineCap,
            LineJoin = Has("/LJ") ? Join(Number("/LJ")) : s.LineJoin,
            MiterLimit = Has("/ML") ? (Number("/ML") is >= 1 ? Number("/ML") : null) : s.MiterLimit,
            Dash = !Has("/D") ? s.Dash : d?.Elements.Count == 2 && PdfObjects.Array(d.Elements[0]) is { } lengths ?
                Dash(lengths.Elements.Select(v => PdfObjects.Resolve(v) is PdfInteger or PdfReal ? PdfObjects.Number(v) : double.NaN),
                    PdfObjects.Resolve(d.Elements[1]) is PdfInteger or PdfReal ? PdfObjects.Number(d.Elements[1]) : null) : null,
            FillOpacity = Has("/ca") ? Alpha(Number("/ca")) : s.FillOpacity,
            StrokeOpacity = Has("/CA") ? Alpha(Number("/CA")) : s.StrokeOpacity,
            BlendMode = Has("/BM") ? Blend(gs.Elements["/BM"]) : s.BlendMode,
            AlphaIsShape = Has("/AIS") ? (PdfObjects.Resolve(gs.Elements["/AIS"]) is PdfBoolean b ? b.Value : null) : s.AlphaIsShape,
            HasSoftMask = Has("/SMask") ? (PdfObjects.Text(gs.Elements["/SMask"]) == "None" ? false : PdfObjects.Dictionary(gs.Elements["/SMask"]) is not null ? true : null) : s.HasSoftMask
        };
    }
    private static double? Nonnegative(double? n) => n is >= 0 ? n : null;
    private static double? Alpha(double? n) => n is >= 0 and <= 1 ? n : null;
    private static PdfLineCap? Cap(double? n) => n is 0 or 1 or 2 ? (PdfLineCap)(int)n : null;
    private static PdfLineJoin? Join(double? n) => n is 0 or 1 or 2 ? (PdfLineJoin)(int)n : null;
    private static PdfDashPattern? Dash(IEnumerable<double> values, double? phase)
    {
        if (phase is null) return null;
        try { return new(values, phase.Value); } catch (ArgumentException) { return null; }
    }
    private static PdfBlendMode? Blend(PdfItem? value)
    {
        if (PdfObjects.Array(value) is { } array)
        {
            foreach (var item in array.Elements) if (BlendName(item) is { } found) return found;
            return null;
        }
        return BlendName(value);
    }
    private static PdfBlendMode? BlendName(PdfItem? value)
    {
        var name = PdfObjects.Text(value);
        if (name == "Compatible") return PdfBlendMode.Normal;
        return Enum.TryParse<PdfBlendMode>(name, out var result) && Enum.IsDefined(result) && name == result.ToString() ? result : null;
    }
}
