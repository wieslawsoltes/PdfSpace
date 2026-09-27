using PdfSharp.Pdf;
using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Pdf;

internal static class NativeAnnotations
{
    public static readonly Dictionary<string, AnnotationKind> Kinds = new()
    {
        ["Highlight"] = AnnotationKind.Highlight, ["Underline"] = AnnotationKind.Underline, ["StrikeOut"] = AnnotationKind.Strikeout,
        ["Ink"] = AnnotationKind.Ink, ["Square"] = AnnotationKind.Rectangle, ["Circle"] = AnnotationKind.Ellipse,
        ["Line"] = AnnotationKind.Line, ["FreeText"] = AnnotationKind.Text, ["Text"] = AnnotationKind.Note, ["Stamp"] = AnnotationKind.Stamp, ["Link"] = AnnotationKind.Link
    };
    public static Annotation? Read(PdfDocument document, PdfDictionary item, SourceGeometry geometry, string key, PdfDestinationResolver? resolver = null)
    {
        if (!Kinds.TryGetValue(PdfObjects.Text(item.Elements["/Subtype"]), out var kind) || item.Elements.ContainsKey("/IRT")) return null;
        var bounds = geometry.ToLogical(PdfObjects.Rectangle(item.Elements["/Rect"]));
        if (bounds.Width < .01 || bounds.Height < .01) return null;
        var color = 0xFF1473E6u;
        if (PdfObjects.Array(item.Elements["/C"]) is { } colors)
        {
            var rgb = colors.Elements.Select(PdfObjects.Number).ToArray();
            if (rgb.Length == 1) rgb = [rgb[0], rgb[0], rgb[0]];
            if (rgb.Length == 4) rgb = [1 - Math.Min(1, rgb[0] + rgb[3]), 1 - Math.Min(1, rgb[1] + rgb[3]), 1 - Math.Min(1, rgb[2] + rgb[3])];
            if (rgb.Length == 3) color = 0xFF000000u | (uint)(Math.Clamp(rgb[0], 0, 1) * 255) << 16 | (uint)(Math.Clamp(rgb[1], 0, 1) * 255) << 8 | (uint)(Math.Clamp(rgb[2], 0, 1) * 255);
        }
        var points = System.Array.Empty<PointD>();
        var coordinates = kind == AnnotationKind.Ink ? PdfObjects.Array(PdfObjects.Array(item.Elements["/InkList"])?.Elements.FirstOrDefault()) : PdfObjects.Array(item.Elements[kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.Strikeout ? "/QuadPoints" : "/L"]);
        if (coordinates is not null)
        {
            var list = new List<PointD>();
            for (var i = 0; i + 1 < coordinates.Elements.Count; i += 2) list.Add(geometry.ToLogical(PdfObjects.Number(coordinates.Elements[i]), PdfObjects.Number(coordinates.Elements[i + 1])));
            points = list.ToArray();
        }
        if (kind == AnnotationKind.Ink && PdfObjects.Array(item.Elements["/InkList"])?.Elements.Count > 1) return null; // Keep multi-stroke originals untouched until the model supports subpaths.
        if (kind == AnnotationKind.Line && PdfObjects.Array(item.Elements["/LE"]) is { Elements.Count: > 1 } ends && PdfObjects.Text(ends.Elements[1]).Contains("Arrow", StringComparison.Ordinal)) kind = AnnotationKind.Arrow;
        string? uri = null; int? target = null;
        if (kind == AnnotationKind.Link)
        {
            var action = PdfObjects.Dictionary(item.Elements["/A"]);
            if (PdfObjects.Text(action?.Elements["/S"]) == "URI")
            {
                uri = PdfObjects.Text(action?.Elements["/URI"]);
                if (!SafeUri(uri)) return null;
            }
            else
            {
                target = (resolver ?? new PdfDestinationResolver(document)).FromAction(item);
                if (target is null) return null;
            }
        }
        var stroke = PdfObjects.Number(PdfObjects.Dictionary(item.Elements["/BS"])?.Elements["/W"]);
        var id = Guid.TryParse(PdfObjects.Text(item.Elements["/NM"]), out var parsed) ? parsed : Guid.NewGuid();
        return new()
        {
            Id = id, SourceKey = key, Kind = kind, Bounds = bounds, Points = points, Color = color,
            Text = PdfObjects.Text(item.Elements["/Contents"]), Author = PdfObjects.Text(item.Elements["/T"]), StrokeWidth = stroke > 0 ? Math.Min(stroke, 100) : 2,
            Created = new DateTimeOffset(DateTime.SpecifyKind(item.Elements.GetDateTime("/CreationDate", DateTime.UtcNow), DateTimeKind.Utc)),
            Resolved = PdfObjects.Text(item.Elements["/State"]) == "Completed", Uri = uri, TargetPage = target
        };
    }
    public static bool SafeUri(string uri) => System.Uri.TryCreate(uri, UriKind.Absolute, out var value) && value.Scheme is "https" or "http" or "mailto";
    public static PdfDictionary Write(PdfDocument document, PdfPage page, Annotation annotation, SourceGeometry geometry, SKTypeface typeface)
    {
        if (annotation.Kind == AnnotationKind.RedactionMark) throw new InvalidOperationException("Pending redactions cannot be saved as an ordinary PDF. Apply raster redaction or save an editable workspace.");
        var result = new PdfDictionary(document); document.Internals.AddObject(result);
        result.Elements.SetName("/Type", "/Annot"); result.Elements.SetName("/Subtype", annotation.Kind switch
        {
            AnnotationKind.Rectangle => "/Square", AnnotationKind.Ellipse => "/Circle", AnnotationKind.Text => "/FreeText", AnnotationKind.Note => "/Text",
            AnnotationKind.Strikeout => "/StrikeOut", AnnotationKind.Arrow or AnnotationKind.Line => "/Line", AnnotationKind.Signature or AnnotationKind.Check => "/Ink", _ => "/" + annotation.Kind
        });
        var box = annotation.Bounds;
        if (annotation.Kind is not (AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.Strikeout or AnnotationKind.Link)) box = box.Inflate(Math.Max(2, annotation.StrokeWidth));
        if (box.Width < .1 || box.Height < .1) box = box.Inflate(1);
        result.Elements.SetRectangle("/Rect", geometry.ToPdf(box));
        result.Elements.SetString("/Contents", annotation.Text); result.Elements.SetString("/T", annotation.Author);
        result.Elements.SetString("/NM", annotation.Id.ToString()); result.Elements.SetInteger("/F", 4); result.Elements["/P"] = page.Reference!;
        result.Elements.SetDateTime("/CreationDate", annotation.Created.UtcDateTime); result.Elements.SetDateTime("/M", DateTime.UtcNow);
        result.Elements["/C"] = PdfObjects.Numbers(document, (annotation.Color >> 16 & 255) / 255.0, (annotation.Color >> 8 & 255) / 255.0, (annotation.Color & 255) / 255.0);
        PdfObjects.DictionaryValue(result, "/BS").Elements.SetReal("/W", annotation.StrokeWidth);
        if (annotation.Kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.Strikeout)
        {
            var points = annotation.Points.Length >= 4 && annotation.Points.Length % 4 == 0 ? annotation.Points : [new PointD(annotation.Bounds.X, annotation.Bounds.Y), new(annotation.Bounds.Right, annotation.Bounds.Y), new(annotation.Bounds.X, annotation.Bounds.Bottom), new(annotation.Bounds.Right, annotation.Bounds.Bottom)];
            result.Elements["/QuadPoints"] = PdfObjects.Numbers(document, points.Select(geometry.ToPdf).SelectMany(p => new[] { p.X, p.Y }).ToArray());
        }
        if (annotation.Kind is AnnotationKind.Ink or AnnotationKind.Signature or AnnotationKind.Check)
        {
            var points = annotation.Kind == AnnotationKind.Check ? new[] { new PointD(annotation.Bounds.X, annotation.Bounds.Center.Y), new(annotation.Bounds.X + annotation.Bounds.Width * .35, annotation.Bounds.Bottom), new(annotation.Bounds.Right, annotation.Bounds.Y) } : annotation.Points;
            var list = new PdfArray(document); list.Elements.Add(PdfObjects.Numbers(document, points.Select(geometry.ToPdf).SelectMany(p => new[] { p.X, p.Y }).ToArray())); result.Elements["/InkList"] = list;
        }
        if (annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            var first = geometry.ToPdf(annotation.Points.FirstOrDefault(new PointD(annotation.Bounds.X, annotation.Bounds.Y))); var last = geometry.ToPdf(annotation.Points.LastOrDefault(new PointD(annotation.Bounds.Right, annotation.Bounds.Bottom)));
            result.Elements["/L"] = PdfObjects.Numbers(document, first.X, first.Y, last.X, last.Y);
            var ends = new PdfArray(document); ends.Elements.Add(new PdfName("/None")); ends.Elements.Add(new PdfName(annotation.Kind == AnnotationKind.Arrow ? "/OpenArrow" : "/None")); result.Elements["/LE"] = ends;
        }
        if (annotation.Kind == AnnotationKind.Text) result.Elements.SetString("/DA", $"/Helv {PdfObjects.F(annotation.FontSize)} Tf 0 g");
        if (annotation.Kind == AnnotationKind.Link)
        {
            result.Elements["/Border"] = PdfObjects.Numbers(document, 0, 0, 0);
            if (annotation.TargetPage is { } target)
            {
                if (target < 0 || target >= document.PageCount) throw new InvalidOperationException("Link target page does not exist.");
                var destination = new PdfArray(document); destination.Elements.Add(document.Pages[target].Reference!); destination.Elements.Add(new PdfName("/Fit")); result.Elements["/Dest"] = destination;
            }
            else if (annotation.Uri is { } uri && SafeUri(uri))
            { var action = new PdfDictionary(document); action.Elements.SetName("/S", "/URI"); action.Elements.SetString("/URI", uri); result.Elements["/A"] = action; }
            else throw new InvalidOperationException("Links must use HTTP, HTTPS, mailto or an existing document page.");
        }
        else PdfObjects.DictionaryValue(result, "/AP").Elements["/N"] = NativeAppearance.ForAnnotation(document, annotation, box, geometry, typeface).Reference!;
        if (annotation.Resolved) { result.Elements.SetString("/State", "Completed"); result.Elements.SetString("/StateModel", "Review"); }
        PdfObjects.ArrayValue(page, "/Annots").Elements.Add(result.Reference!);
        foreach (var reply in annotation.Replies)
        {
            var child = new PdfDictionary(document); document.Internals.AddObject(child);
            child.Elements.SetName("/Type", "/Annot"); child.Elements.SetName("/Subtype", "/Text"); child.Elements.SetName("/RT", "/R"); child.Elements["/IRT"] = result.Reference!;
            child.Elements.SetString("/Contents", reply.Text); child.Elements.SetString("/T", reply.Author); child.Elements.SetString("/NM", reply.Id.ToString()); child.Elements.SetDateTime("/CreationDate", reply.Created.UtcDateTime);
            child.Elements.SetInteger("/F", 2); child.Elements.SetRectangle("/Rect", geometry.ToPdf(box)); PdfObjects.ArrayValue(page, "/Annots").Elements.Add(child.Reference!);
        }
        return result;
    }
}
