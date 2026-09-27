using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class BrowserOcrVerification
{
    public static void Run(string directory)
    {
        var input = File.ReadAllBytes(Path.Combine(directory, "ocr-input.pdf"));
        var output = File.ReadAllBytes(Path.Combine(directory, "ocr-reviewed.pdf"));
        using var original = UglyToad.PdfPig.PdfDocument.Open(input);
        using var recognized = UglyToad.PdfPig.PdfDocument.Open(output);
        if (original.GetPage(1).Text.Length != 0 || !recognized.GetPage(1).Text.Contains("RESTORED") || !recognized.GetPage(1).Text.Contains("Circular"))
            throw new InvalidDataException("Browser OCR output does not contain expected real PDF text.");
        var before = PdfDocumentEngine.Open(input, "before.pdf"); var after = PdfDocumentEngine.Open(output, "after.pdf");
        using var renderer = new PdfRenderer(); using var a = SKBitmap.Decode(renderer.ExportPng(before, 0, .5)); using var b = SKBitmap.Decode(renderer.ExportPng(after, 0, .5));
        if (!a.Pixels.SequenceEqual(b.Pixels)) throw new InvalidDataException("Browser OCR changed visible scan pixels.");
        Console.WriteLine("PASS independent native verification of real browser OCR Unicode text, correction and unchanged pixels");
    }
}
