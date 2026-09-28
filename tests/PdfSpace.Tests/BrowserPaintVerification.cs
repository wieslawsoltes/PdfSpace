using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class BrowserPaintVerification
{
    public static void Run(string directory)
    {
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); passed++; Console.WriteLine("PASS " + message); }
        PdfSpace.Core.PdfWorkspace Open(string name) => PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, name)), name);
        var styled = Open("paint-browser-styled.pdf");
        var path = PdfObjectEditor.Read(styled, 0).First(o => o.Kind == PdfPageObjectKind.Path).Paint;
        Check(path.LineCap == PdfLineCap.Round && path.LineJoin == PdfLineJoin.Bevel && path.StrokeWidth == 8 && path.MiterLimit == 4,
            "browser path stroke properties survive native reading");
        Check(path.Dash?.Lengths.SequenceEqual([9d, 3, 1]) == true && path.Dash.Phase == 2 && path.FillOpacity == .5 && path.StrokeOpacity == .6 && path.BlendMode == PdfBlendMode.Multiply,
            "browser odd dash, opacity and blend persist in native content");
        using var renderer = new PdfRenderer();
        using (var image = SKBitmap.Decode(renderer.ExportPng(styled, 0, 1)))
            Check(image.GetPixel(70, 380).Red > 120, "browser path opacity renders rather than merely updating metadata");
        var text = Open("paint-browser-text.pdf");
        var paints = PdfObjectEditor.Read(text, 0).Where(o => o.Kind == PdfPageObjectKind.Text).Select(o => o.Paint).ToArray();
        Check(paints[0].FillOpacity == .45 && paints[0].StrokeOpacity == .55 && paints[0].BlendMode == PdfBlendMode.Screen,
            "browser applies alpha to text with changing internal graphics state");
        Check(paints[1].FillOpacity == .8 && paints[1].StrokeOpacity == .9 && paints[1].BlendMode == PdfBlendMode.Normal,
            "browser text paint edits preserve following native state");
        var masked = Open("paint-browser-image.pdf");
        using (var image = SKBitmap.Decode(renderer.ExportPng(masked, 0, 10)))
        {
            var pixel = image.GetPixel(image.Width / 2, image.Height / 2);
            Check(pixel.Red is >= 244 and <= 250 && pixel.Green is >= 198 and <= 207 && pixel.Blue is >= 195 and <= 205,
                "browser image soft mask and nonstroking opacity combine in renderer");
        }
        Console.WriteLine($"{passed} browser paint verification checks passed.");
    }
}
