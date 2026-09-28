using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class BrowserPhotoVerification
{
    public static void Run(string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
        byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(directory, name));
        var bytes = Bytes("photo-browser-jpeg.pdf");
        using (var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify))
        {
            var image = pdf.Internals.GetAllObjects().OfType<PdfDictionary>().Single(item => item.Elements.GetName("/Subtype") == "/Image");
            Check(image.Elements.GetName("/Filter") == "/DCTDecode" && image.Stream.Value.SequenceEqual(PhotoImportTests.Photo()), "browser retains original JPEG samples with DCTDecode");
        }
        var oriented = PdfDocumentEngine.Open(Bytes("photo-browser-oriented.pdf"), "oriented.pdf");
        var occurrence = PdfImageEditor.Read(oriented, 0).Single();
        Check(occurrence.PixelWidth == 80 && occurrence.PixelHeight == 120, "browser normalizes rotated photo sample dimensions");
        using (var renderer = new PdfRenderer())
        using (var image = SKBitmap.Decode(renderer.ExportPng(oriented, 0, 4)))
        {
            var topLeft = image.GetPixel(image.Width / 4, image.Height / 4);
            Check(topLeft.Blue > 180 && topLeft.Red < 60, "native renderer confirms rotated photograph orientation from browser export");
        }
        var duplicated = PdfDocumentEngine.Open(Bytes("photo-browser-duplicated.pdf"), "duplicated.pdf");
        var images = PdfImageEditor.Read(duplicated, 0);
        var expectedHeight = 160d * 24 / 40; // NativeObjectSample has 40 x 24 raster samples.
        Check(images.Length == 3 && Math.Abs(images[0].Bounds.Height - expectedHeight) < .01 &&
            images[0].PixelWidth == 40 && images[0].PixelHeight == 24, "native reader verifies browser duplication and proportion correction");
        Check(PdfImageEditor.Read(duplicated, 1).Single().Bounds.Width == 160, "browser duplication leaves the other shared-resource page unchanged");
        Console.WriteLine("5 browser photo verification checks passed.");
    }
}
