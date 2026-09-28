using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class RendererPaintTests
{
    public static void Run(Action<bool, string> check)
    {
        var odd = Draw("3 w 0 J [9 3 1] 2 d 20 70 m 360 70 l S");
        var doubled = Draw("3 w 0 J [9 3 1 9 3 1] 2 d 20 70 m 360 70 l S");
        check(Render(odd).SequenceEqual(Render(doubled)), "Skia repeats the complete odd dash array instead of dropping an interval");
        check(Render(Draw("3 w [] 19 d 20 70 m 360 70 l S")).SequenceEqual(Render(Draw("3 w [] 0 d 20 70 m 360 70 l S"))),
            "empty dash pattern remains solid regardless of phase");
        var dots = Draw("5 w 1 J [0 9] 0 d 20 70 m 360 70 l S");
        using (var image = SKBitmap.Decode(Render(dots)))
            check(image.GetPixel(20, 110).Red < 60 && image.GetPixel(24, 110).Red > 230,
                "zero-length on intervals render round-cap dots without fabricated dash lengths");

        const string shape = "30 40 m 170 110 l 45 35 l S\n";
        var bevel = Draw("8 w 0 j 1 M " + shape);
        var miter = Draw("8 w 0 j 30 M " + shape);
        check(!Render(bevel).SequenceEqual(Render(miter)), "native miter limit affects Skia stroke geometry");
        var defaultMiter = Draw("8 w 0 j " + shape);
        check(Render(defaultMiter).SequenceEqual(Render(Draw("8 w 0 j 10 M " + shape))), "default PDF miter limit remains ten");
        var combined = Draw("8 w 0 j 1 M " + shape + "q 1 0 0 1 0 70 cm 30 M " + shape + "Q");
        using (var expected = SKBitmap.Decode(Render(miter)))
        using (var actual = SKBitmap.Decode(Render(combined)))
        {
            var same = true;
            // Top translated stroke must use its own miter-keyed paint, not the first cached bevel.
            for (var y = 0; y < 60; y++) for (var x = 195; x < 300; x++)
                same &= expected.GetPixel(x, y + 70) == actual.GetPixel(x, y);
            check(same, "different miter limits cannot alias a cached stroke paint");
        }

        var images = Images();
        using (var image = SKBitmap.Decode(Render(images)))
        {
            var a = image.GetPixel(40, 140); var b = image.GetPixel(120, 140); var c = image.GetPixel(200, 140);
            check(a.Red > 240 && a.Green is >= 188 and <= 194 && b.Green is >= 60 and <= 67 && c.Green < 5,
                "same-page images apply distinct nonstroking opacity and do not alias cached image paints");
        }
        var shaded = Shading();
        using (var image = SKBitmap.Decode(Render(shaded)))
            check(image.GetPixel(40, 140).Green is >= 185 and <= 195 && image.GetPixel(140, 140).Green is >= 58 and <= 68,
                "shading placement opacity is visible through the native renderer");
        Directory.CreateDirectory("artifacts/structured");
        foreach (var (name, document) in new[] { ("dash-odd", odd), ("dash-doubled", doubled), ("miter-bevel", bevel), ("miter-extended", miter), ("image-alpha", images), ("shading-alpha", shaded) })
        {
            File.WriteAllBytes("artifacts/structured/renderer-" + name + ".pdf", PdfDocumentEngine.Save(document, SKTypeface.Default).Bytes);
            File.WriteAllBytes("artifacts/structured/renderer-" + name + ".png", Render(document));
        }
    }
    private static PdfWorkspace Draw(string operations)
    {
        using var pdf = new PdfDocument(); var page = Page(pdf);
        return Finish(pdf, page, operations);
    }
    private static PdfWorkspace Images()
    {
        using var pdf = new PdfDocument(); var page = Page(pdf);
        var resource = new PdfDictionary(pdf); resource.Elements.SetName("/Type", "/XObject"); resource.Elements.SetName("/Subtype", "/Image");
        resource.Elements.SetInteger("/Width", 1); resource.Elements.SetInteger("/Height", 1); resource.Elements.SetInteger("/BitsPerComponent", 8);
        resource.Elements.SetName("/ColorSpace", "/DeviceRGB"); resource.CreateStream([255, 0, 0]); pdf.Internals.AddObject(resource);
        var objects = new PdfDictionary(pdf); objects.Elements["/I"] = resource.Reference!;
        page.Resources.Elements["/XObject"] = objects; Alpha(pdf, page);
        return Finish(pdf, page, "q /A gs 50 0 0 50 20 20 cm /I Do Q q /B gs 50 0 0 50 100 20 cm /I Do Q q 50 0 0 50 180 20 cm /I Do Q");
    }
    private static PdfWorkspace Shading()
    {
        using var pdf = new PdfDocument(); var page = Page(pdf);
        var function = new PdfDictionary(pdf); function.Elements.SetInteger("/FunctionType", 2);
        function.Elements["/Domain"] = new PdfArray(pdf, new PdfInteger(0), new PdfInteger(1));
        function.Elements["/C0"] = new PdfArray(pdf, new PdfInteger(1), new PdfInteger(0), new PdfInteger(0));
        function.Elements["/C1"] = new PdfArray(pdf, new PdfInteger(1), new PdfInteger(0), new PdfInteger(0)); function.Elements.SetInteger("/N", 1);
        var shade = new PdfDictionary(pdf); shade.Elements.SetInteger("/ShadingType", 2); shade.Elements.SetName("/ColorSpace", "/DeviceRGB");
        shade.Elements["/Coords"] = new PdfArray(pdf, new PdfInteger(0), new PdfInteger(0), new PdfInteger(400), new PdfInteger(0));
        shade.Elements["/Function"] = function;
        var shades = new PdfDictionary(pdf); shades.Elements["/S"] = shade; page.Resources.Elements["/Shading"] = shades;
        Alpha(pdf, page);
        return Finish(pdf, page, "q 20 20 50 50 re W n /A gs /S sh Q q 120 20 50 50 re W n /B gs /S sh Q");
    }
    private static void Alpha(PdfDocument pdf, PdfPage page)
    {
        var a = new PdfDictionary(pdf); a.Elements.SetReal("/ca", .25);
        var b = new PdfDictionary(pdf); b.Elements.SetReal("/ca", .75);
        var states = new PdfDictionary(pdf); states.Elements["/A"] = a; states.Elements["/B"] = b;
        page.Resources.Elements["/ExtGState"] = states;
    }
    private static PdfPage Page(PdfDocument pdf)
    { pdf.Version = 14; var p = pdf.AddPage(); p.Width = XUnit.FromPoint(400); p.Height = XUnit.FromPoint(180); return p; }
    private static PdfWorkspace Finish(PdfDocument pdf, PdfPage page, string operations)
    {
        var content = new PdfDictionary(pdf); content.CreateStream(Encoding.ASCII.GetBytes(operations)); pdf.Internals.AddObject(content);
        page.Elements["/Contents"] = content.Reference!; using var stream = new MemoryStream(); pdf.Save(stream, false);
        return PdfDocumentEngine.Open(stream.ToArray(), "renderer.pdf");
    }
    private static byte[] Render(PdfWorkspace document) { using var renderer = new PdfRenderer(); return renderer.ExportPng(document, 0, 1); }
}
