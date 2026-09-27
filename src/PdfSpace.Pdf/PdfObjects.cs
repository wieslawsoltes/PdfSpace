using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSpace.Core;
namespace PdfSpace.Pdf;

internal static class PdfObjects
{
    public static PdfItem? Resolve(PdfItem? item)
    {
        for (var i = 0; item is PdfReference reference; i++)
        {
            if (i >= 32) throw new InvalidDataException("Cyclic or excessive PDF object references.");
            item = reference.Value;
        }
        return item;
    }
    public static PdfDictionary? Dictionary(PdfItem? item) => Resolve(item) as PdfDictionary;
    public static PdfArray? Array(PdfItem? item) => Resolve(item) as PdfArray;
    public static string Text(PdfItem? item) => Resolve(item) switch { PdfString text => text.Value, PdfName name => name.Value.TrimStart('/'), _ => "" };
    public static double Number(PdfItem? item) => Resolve(item) switch { PdfInteger value => value.Value, PdfReal value => value.Value, _ => 0 };
    public static PdfItem? Inherited(PdfDictionary dictionary, string key)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (PdfDictionary? current = dictionary; current is not null; current = Dictionary(current.Elements["/Parent"]))
        {
            if (!visited.Add(current) || visited.Count > 64) throw new InvalidDataException("Cyclic PDF field hierarchy.");
            if (current.Elements[key] is { } value) return Resolve(value);
        }
        return null;
    }
    public static PdfDictionary Terminal(PdfDictionary widget) => widget.Elements.ContainsKey("/T") ? widget : Dictionary(widget.Elements["/Parent"]) ?? widget;
    public static string FullName(PdfDictionary widget)
    {
        var parts = new List<string>(); var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (PdfDictionary? node = widget; node is not null; node = Dictionary(node.Elements["/Parent"]))
        {
            if (!visited.Add(node) || visited.Count > 64) throw new InvalidDataException("Cyclic field hierarchy.");
            var name = Text(node.Elements["/T"]); if (name.Length > 0) parts.Insert(0, name);
        }
        return string.Join('.', parts);
    }
    public static PdfArray Numbers(PdfDocument document, params double[] values)
    {
        var array = new PdfArray(document);
        foreach (var value in values) array.Elements.Add(new PdfReal(double.IsFinite(value) ? value : throw new InvalidDataException("Nonfinite PDF coordinate.")));
        return array;
    }
    public static PdfArray ArrayValue(PdfDictionary parent, string key)
    {
        if (Array(parent.Elements[key]) is { } existing) return existing;
        var array = new PdfArray(parent.Owner); parent.Elements[key] = array; return array;
    }
    public static PdfDictionary DictionaryValue(PdfDictionary parent, string key)
    {
        if (Dictionary(parent.Elements[key]) is { } existing) return existing;
        var dictionary = new PdfDictionary(parent.Owner); parent.Elements[key] = dictionary; return dictionary;
    }
    public static string F(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    public static PdfRectangle Rectangle(PdfItem? value)
    {
        var array = Array(value);
        if (array?.Elements.Count != 4) throw new InvalidDataException("Invalid PDF rectangle.");
        var x1 = Number(array.Elements[0]); var y1 = Number(array.Elements[1]); var x2 = Number(array.Elements[2]); var y2 = Number(array.Elements[3]);
        return new(new PdfSharp.Drawing.XPoint(Math.Min(x1, x2), Math.Min(y1, y2)), new PdfSharp.Drawing.XPoint(Math.Max(x1, x2), Math.Max(y1, y2)));
    }
}

/// <summary>PDF default-user-space to normalized top-left page coordinates, including original crop and rotation.</summary>
internal sealed class SourceGeometry(PdfPage page)
{
    public PdfRectangle Box { get; } = page.EffectiveCropBoxReadOnly;
    public int Rotation { get; } = ((page.Rotate % 360) + 360) % 360;
    public PointD ToLogical(double x, double y) => Rotation switch
    {
        90 => new(y - Box.Y1, x - Box.X1),
        180 => new(Box.X2 - x, y - Box.Y1),
        270 => new(Box.Y2 - y, Box.X2 - x),
        _ => new(x - Box.X1, Box.Y2 - y)
    };
    public PointD ToPdf(PointD point) => Rotation switch
    {
        90 => new(Box.X1 + point.Y, Box.Y1 + point.X),
        180 => new(Box.X2 - point.X, Box.Y1 + point.Y),
        270 => new(Box.X2 - point.Y, Box.Y2 - point.X),
        _ => new(Box.X1 + point.X, Box.Y2 - point.Y)
    };
    public RectD ToLogical(PdfRectangle rect) => RectD.Between(ToLogical(rect.X1, rect.Y1), ToLogical(rect.X2, rect.Y2));
    public PdfRectangle ToPdf(RectD rect)
    {
        var a = ToPdf(new PointD(rect.X, rect.Y)); var b = ToPdf(new PointD(rect.Right, rect.Bottom));
        return new(new PdfSharp.Drawing.XPoint(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new PdfSharp.Drawing.XPoint(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }
    public string AppearanceMatrix(double width, double height) => Rotation switch
    {
        90 => "0 1 1 0 0 0 cm\n",
        180 => $"-1 0 0 1 {PdfObjects.F(width)} 0 cm\n",
        270 => $"0 -1 -1 0 {PdfObjects.F(height)} {PdfObjects.F(width)} cm\n",
        _ => $"1 0 0 -1 0 {PdfObjects.F(height)} cm\n"
    };
}
