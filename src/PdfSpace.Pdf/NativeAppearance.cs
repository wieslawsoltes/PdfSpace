using System.Text;
using PdfSharp.Pdf;
using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Pdf;

/// <summary>Font-independent PDF appearance streams. Text outlines use the same Skia typeface as the viewport.</summary>
internal static class NativeAppearance
{
    public static PdfDictionary ForAnnotation(PdfDocument document, Annotation annotation, RectD box, SourceGeometry geometry, SKTypeface typeface)
    {
        var b = new StringBuilder(); var color = annotation.Color;
        b.Append($"{PdfObjects.F((color >> 16 & 255) / 255.0)} {PdfObjects.F((color >> 8 & 255) / 255.0)} {PdfObjects.F((color & 255) / 255.0)} rg\n");
        b.Append($"{PdfObjects.F((color >> 16 & 255) / 255.0)} {PdfObjects.F((color >> 8 & 255) / 255.0)} {PdfObjects.F((color & 255) / 255.0)} RG\n{PdfObjects.F(annotation.StrokeWidth)} w 1 J 1 j\n/GS gs\n");
        var r = annotation.Bounds.Translate(new(-box.X, -box.Y));
        string Pt(PointD p) => $"{PdfObjects.F(p.X - box.X)} {PdfObjects.F(p.Y - box.Y)}";
        void Line(PointD a, PointD z) => b.Append($"{Pt(a)} m {Pt(z)} l S\n");
        switch (annotation.Kind)
        {
            case AnnotationKind.Highlight:
                if (annotation.Points.Length >= 4 && annotation.Points.Length % 4 == 0)
                    for (var i = 0; i < annotation.Points.Length; i += 4)
                        b.Append($"{Pt(annotation.Points[i])} m {Pt(annotation.Points[i + 1])} l {Pt(annotation.Points[i + 3])} l {Pt(annotation.Points[i + 2])} l h f\n");
                else b.Append(Rect(r) + " f\n");
                break;
            case AnnotationKind.Rectangle: b.Append(Rect(r) + " S\n"); break;
            case AnnotationKind.Link: break;
            case AnnotationKind.Ellipse: Oval(b, r); b.Append("S\n"); break;
            case AnnotationKind.Underline: Line(new(annotation.Bounds.X, annotation.Bounds.Bottom), new(annotation.Bounds.Right, annotation.Bounds.Bottom)); break;
            case AnnotationKind.Strikeout: Line(new(annotation.Bounds.X, annotation.Bounds.Center.Y), new(annotation.Bounds.Right, annotation.Bounds.Center.Y)); break;
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                var start = annotation.Points.FirstOrDefault(new PointD(annotation.Bounds.X, annotation.Bounds.Y)); var end = annotation.Points.LastOrDefault(new PointD(annotation.Bounds.Right, annotation.Bounds.Bottom));
                Line(start, end);
                if (annotation.Kind == AnnotationKind.Arrow)
                {
                    var angle = Math.Atan2(end.Y - start.Y, end.X - start.X); var length = Math.Max(8, annotation.StrokeWidth * 4);
                    foreach (var offset in new[] { -.5, .5 }) Line(end, new(end.X - length * Math.Cos(angle + offset), end.Y - length * Math.Sin(angle + offset)));
                }
                break;
            case AnnotationKind.Ink:
            case AnnotationKind.Signature:
                if (annotation.Points.Length > 0)
                { b.Append(Pt(annotation.Points[0]) + " m\n"); foreach (var point in annotation.Points.Skip(1)) b.Append(Pt(point) + " l\n"); b.Append("S\n"); }
                break;
            case AnnotationKind.Note:
                b.Append(Rect(r) + " f\n1 1 1 RG 1 w\n");
                b.Append($"{PdfObjects.F(r.X + 5)} {PdfObjects.F(r.Y + 7)} m {PdfObjects.F(r.Right - 5)} {PdfObjects.F(r.Y + 7)} l S\n"); break;
            case AnnotationKind.Check:
                b.Append($"{PdfObjects.F(r.X)} {PdfObjects.F(r.Center.Y)} m {PdfObjects.F(r.X + r.Width * .35)} {PdfObjects.F(r.Bottom)} l {PdfObjects.F(r.Right)} {PdfObjects.F(r.Y)} l S\n"); break;
            case AnnotationKind.Stamp:
                b.Append(Rect(r) + " S\n"); Text(b, annotation.Text, r.X + 10, r.Y + 7, annotation.FontSize, Math.Max(1, r.Width - 20), typeface); break;
            case AnnotationKind.Text: Text(b, annotation.Text, r.X, r.Y, annotation.FontSize, Math.Max(1, r.Width), typeface); break;
        }
        return Create(document, b.ToString(), box.Width, box.Height, geometry, annotation.Kind == AnnotationKind.Highlight ? .3 : (annotation.Color >> 24) / 255.0, annotation.Kind == AnnotationKind.Highlight);
    }
    public static PdfDictionary ForField(PdfDocument document, PdfFormFieldState field, SourceGeometry geometry, SKTypeface typeface)
    {
        var b = new StringBuilder("1 1 1 rg\n"); var rect = new RectD(.5, .5, Math.Max(1, field.Bounds.Width - 1), Math.Max(1, field.Bounds.Height - 1));
        b.Append(Rect(rect) + " f\n0.53 0.55 0.57 RG 0.7 w\n");
        if (field.Kind == PdfFieldKind.RadioButton) { Oval(b, rect); b.Append("S\n"); } else b.Append(Rect(rect) + " S\n");
        b.Append("0.08 0.12 0.17 rg 0.08 0.12 0.17 RG\n");
        if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
        {
            if (field.IsChecked)
            {
                if (field.Kind == PdfFieldKind.RadioButton) { Oval(b, new(rect.Width * .26, rect.Height * .26, rect.Width * .48, rect.Height * .48)); b.Append("f\n"); }
                else b.Append($"{PdfObjects.F(Math.Max(1, rect.Height / 9))} w {PdfObjects.F(rect.Width * .2)} {PdfObjects.F(rect.Height * .5)} m {PdfObjects.F(rect.Width * .43)} {PdfObjects.F(rect.Height * .78)} l {PdfObjects.F(rect.Width * .84)} {PdfObjects.F(rect.Height * .22)} l S\n");
            }
        }
        else Text(b, field.Options.FirstOrDefault(option => option.Value == field.Value)?.Label ?? field.Value, 4, 3, field.FontSize, Math.Max(1, rect.Width - 8), typeface);
        return Create(document, b.ToString(), field.Bounds.Width, field.Bounds.Height, geometry);
    }
    private static PdfDictionary Create(PdfDocument document, string content, double width, double height, SourceGeometry geometry, double alpha = 1, bool multiply = false)
    {
        var form = new PdfDictionary(document); document.Internals.AddObject(form);
        form.Elements.SetName("/Type", "/XObject"); form.Elements.SetName("/Subtype", "/Form"); form.Elements.SetInteger("/FormType", 1);
        form.Elements["/BBox"] = geometry.Rotation % 180 == 0 ? PdfObjects.Numbers(document, 0, 0, width, height) : PdfObjects.Numbers(document, 0, 0, height, width);
        var resources = PdfObjects.DictionaryValue(form, "/Resources"); var gs = new PdfDictionary(document);
        gs.Elements.SetReal("/ca", alpha); gs.Elements.SetReal("/CA", alpha); if (multiply) gs.Elements.SetName("/BM", "/Multiply");
        PdfObjects.DictionaryValue(resources, "/ExtGState").Elements["/GS"] = gs;
        form.CreateStream(Encoding.ASCII.GetBytes("q\n" + geometry.AppearanceMatrix(width, height) + $"0 0 {PdfObjects.F(width)} {PdfObjects.F(height)} re W n\n" + content + "Q\n"));
        return form;
    }
    private static string Rect(RectD r) => $"{PdfObjects.F(r.X)} {PdfObjects.F(r.Y)} {PdfObjects.F(r.Width)} {PdfObjects.F(r.Height)} re";
    private static void Oval(StringBuilder b, RectD r)
    {
        var cx = r.Center.X; var cy = r.Center.Y; var rx = r.Width / 2; var ry = r.Height / 2; const double k = .552284749831;
        void P(double x, double y) => b.Append(PdfObjects.F(x) + " " + PdfObjects.F(y) + " ");
        P(cx + rx, cy); b.Append("m ");
        P(cx + rx, cy + k * ry); P(cx + k * rx, cy + ry); P(cx, cy + ry); b.Append("c ");
        P(cx - k * rx, cy + ry); P(cx - rx, cy + k * ry); P(cx - rx, cy); b.Append("c ");
        P(cx - rx, cy - k * ry); P(cx - k * rx, cy - ry); P(cx, cy - ry); b.Append("c ");
        P(cx + k * rx, cy - ry); P(cx + rx, cy - k * ry); P(cx + rx, cy); b.Append("c h\n");
    }
    private static void Text(StringBuilder b, string text, double x, double y, double size, double width, SKTypeface typeface)
    {
        using var font = new SKFont(typeface, (float)size);
        var baseline = (float)y - font.Metrics.Ascent;
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var current = "";
            foreach (var word in line.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.MeasureText(candidate) > width)
                { using var path = font.GetTextPath(current, new SKPoint((float)x, baseline)); Path(b, path); baseline += (float)size * 1.35f; current = word; }
                else current = candidate;
            }
            using var last = font.GetTextPath(current, new SKPoint((float)x, baseline)); Path(b, last); baseline += (float)size * 1.35f;
        }
    }
    private static void Path(StringBuilder b, SKPath path)
    {
        using var iterator = path.CreateRawIterator(); var p = new SKPoint[4];
        void P(SKPoint point) => b.Append(PdfObjects.F(point.X) + " " + PdfObjects.F(point.Y) + " ");
        for (var verb = iterator.Next(p); verb != SKPathVerb.Done; verb = iterator.Next(p))
        {
            switch (verb)
            {
                case SKPathVerb.Move: P(p[0]); b.Append("m\n"); break;
                case SKPathVerb.Line: P(p[1]); b.Append("l\n"); break;
                case SKPathVerb.Cubic: P(p[1]); P(p[2]); P(p[3]); b.Append("c\n"); break;
                case SKPathVerb.Quad:
                    P(new(p[0].X + (p[1].X - p[0].X) * 2 / 3, p[0].Y + (p[1].Y - p[0].Y) * 2 / 3));
                    P(new(p[2].X + (p[1].X - p[2].X) * 2 / 3, p[2].Y + (p[1].Y - p[2].Y) * 2 / 3)); P(p[2]); b.Append("c\n"); break;
                case SKPathVerb.Close: b.Append("h\n"); break;
                case SKPathVerb.Conic: throw new NotSupportedException("Conic font outlines cannot be exported by this appearance encoder.");
            }
        }
        b.Append(path.FillType is SKPathFillType.EvenOdd ? "f*\n" : "f\n");
    }
}
