using PdfSpace.Documents;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class BrowserObjectVerification
{
    public static void Run(string directory)
    {
        var edited = PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, "objects-browser-edited.pdf")), "browser.pdf");
        var original = NativeObjectSample.Create();
        var images = PdfImageEditor.Read(edited, 0);
        void Check(bool value, string label) { if (!value) throw new InvalidDataException(label); Console.WriteLine("PASS " + label); }
        Check(images.Length == 2 && images[0].PixelWidth == 100 && images[0].PixelHeight == 60, "browser PDF contains the replacement as a native image resource");
        Check(Math.Abs(images[0].Bounds.Width - 180) < .01 && Math.Abs(images[0].Bounds.Y - 250) < 2, "browser pointer and inspector edits persist in PDF content");
        var other = PdfImageEditor.Read(edited, 1);
        Check(other.Length == 1 && other[0].PixelWidth == 40 && other[0].Bounds.Width == 160, "browser edits leave shared source images on the other page unchanged");
        using var renderer = new PdfRenderer();
        using var before = SKBitmap.Decode(renderer.ExportPng(original, 1, 1));
        using var after = SKBitmap.Decode(renderer.ExportPng(edited, 1, 1));
        Check(before.GetPixelSpan().SequenceEqual(after.GetPixelSpan()), "native rendering independently verifies the untouched second page after browser edits");
        var unicode = PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, "objects-browser-unicode.pdf")), "unicode.pdf");
        Check(PdfReader.Find(unicode, "Żółć café – Review").Count() == 1, "browser replacement font exports searchable native Unicode text");
        Check(PdfReader.Find(unicode, "Shared image and text").Count() == 2, "browser text edit retains the sibling and other-page occurrences");
        Console.WriteLine("6 browser native-object verification checks passed.");
    }
}
