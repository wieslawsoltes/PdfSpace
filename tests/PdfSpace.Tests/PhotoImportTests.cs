using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class PhotoImportTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var jpeg = Photo();
        var document = PdfImageEditor.OpenImage(jpeg, "Camera.jpg", 72);
        using (var pdf = Open(document))
        {
            var image = Images(pdf).Single();
            check(image.Elements.GetName("/Filter") == "/DCTDecode", "eligible JPEG imports as native DCT without recompression");
            check(image.Stream.Value.SequenceEqual(jpeg), "JPEG passthrough retains every original compressed byte");
            check(!image.Elements.ContainsKey("/SMask"), "opaque JPEG import does not allocate a soft mask");
        }
        check(document.Pages[0].Width == 120 && document.Pages[0].Height == 80, "photo page dimensions use requested DPI");
        using var original = SKBitmap.Decode(jpeg);
        for (var orientation = 1; orientation <= 8; orientation++)
        {
            var encoded = Exif(jpeg, orientation);
            var oriented = PdfImageEditor.OpenImage(encoded, $"EXIF-{orientation}.jpg", 72);
            var swapped = orientation >= 5;
            check(oriented.Pages[0].Width == (swapped ? 80 : 120) && oriented.Pages[0].Height == (swapped ? 120 : 80), $"EXIF {orientation}: oriented page dimensions");
            using var renderer = new PdfRenderer();
            using var rendered = SKBitmap.Decode(renderer.ExportPng(oriented, 0, 1));
            var equal = true;
            foreach (var (x, y) in new[] { (10, 10), (rendered.Width - 11, 10), (10, rendered.Height - 11), (rendered.Width - 11, rendered.Height - 11) })
            {
                var (sx, sy) = orientation switch
                {
                    2 => (119 - x, y), 3 => (119 - x, 79 - y), 4 => (x, 79 - y), 5 => (y, x),
                    6 => (y, 79 - x), 7 => (119 - y, 79 - x), 8 => (119 - y, x), _ => (x, y)
                };
                equal &= Near(original.GetPixel(sx, sy), rendered.GetPixel(x, y));
            }
            check(equal, $"EXIF {orientation}: independent page rendering matches expected corner colors");
            var replace = PdfImageEditor.Replace(document, PdfImageEditor.Read(document, 0)[0], encoded);
            var occurrence = PdfImageEditor.Read(replace, 0)[0];
            check(occurrence.PixelWidth == (swapped ? 80 : 120) && occurrence.PixelHeight == (swapped ? 120 : 80) && occurrence.Bounds == new RectD(0, 0, 120, 80), $"EXIF {orientation}: replacement normalizes samples while retaining placement");
            File.WriteAllBytes($"artifacts/structured/photo-exif-{orientation}.pdf", PdfDocumentEngine.Save(oriented, SKTypeface.Default).Bytes);
        }
        {
            // Synthetic sRGB ICC profile generated with LittleCMS for this fixture.
            var profileBytes = Convert.FromBase64String("AAACTGxjbXMEQAAAbW50clJHQiBYWVogB+oACQAcAAgALwAIYWNzcEFQUEwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1sY21zAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALZGVzYwAAAQgAAAA2Y3BydAAAAUAAAABMd3RwdAAAAYwAAAAUY2hhZAAAAaAAAAAsclhZWgAAAcwAAAAUYlhZWgAAAeAAAAAUZ1hZWgAAAfQAAAAUclRSQwAAAggAAAAgZ1RSQwAAAggAAAAgYlRSQwAAAggAAAAgY2hybQAAAigAAAAkbWx1YwAAAAAAAAABAAAADGVuVVMAAAAaAAAAHABzAFIARwBCACAAYgB1AGkAbAB0AC0AaQBuAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAADAAAAAcAE4AbwAgAGMAbwBwAHkAcgBpAGcAaAB0ACwAIAB1AHMAZQAgAGYAcgBlAGUAbAB5WFlaIAAAAAAAAPbWAAEAAAAA0y1zZjMyAAAAAAABDEIAAAXe///zJQAAB5MAAP2Q///7of///aIAAAPcAADAblhZWiAAAAAAAABvoAAAOPUAAAOQWFlaIAAAAAAAACSfAAAPhAAAtsNYWVogAAAAAAAAYpcAALeHAAAY2XBhcmEAAAAAAAMAAAACZmYAAPKnAAANWQAAE9AAAApbY2hybQAAAAAAAwAAAACj1wAAVHsAAEzNAACZmgAAJmYAAA9c");
            var payload = new byte[14 + profileBytes.Length]; "ICC_PROFILE\0"u8.CopyTo(payload); payload[12] = 1; payload[13] = 1;
            profileBytes.CopyTo(payload, 14);
            var segment = new byte[4 + payload.Length]; segment[0] = 255; segment[1] = 226;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), checked((ushort)(payload.Length + 2))); payload.CopyTo(segment, 4);
            byte[] profiled = [..jpeg.AsSpan(0, 2), ..segment, ..jpeg.AsSpan(2)];
            var normalized = PdfImageEditor.OpenImage(profiled, "profiled.jpg", 72);
            using var pdf = Open(normalized);
            check(Images(pdf).Single().Elements.GetName("/Filter") == "/FlateDecode", "ICC-profiled JPEG uses the color-managed fallback rather than guessed DeviceRGB passthrough");
            using var renderer = new PdfRenderer(); using var rendered = SKBitmap.Decode(renderer.ExportPng(normalized, 0, 1));
            check(Near(rendered.GetPixel(10, 10), original.GetPixel(10, 10)), "sRGB-profiled photo retains expected colors through normalization");
        }
        var transparent = TransparentPng();
        var rgbaDocument = PdfImageEditor.OpenImage(transparent, "alpha.png", 72);
        using (var pdf = Open(rgbaDocument))
        {
            var image = Images(pdf).Single(i => i.Elements.GetName("/ColorSpace") == "/DeviceRGB");
            var mask = ((PdfReference)image.Elements["/SMask"]).Value as PdfDictionary;
            check(mask is not null && mask.Stream.UnfilteredValue[0] == 64 && mask.Stream.UnfilteredValue[1] == 255, "streamed PNG alpha produces the correct native soft mask");
            var rgb = image.Stream.UnfilteredValue;
            check(rgb[0] == 240 && rgb[1] == 40 && rgb[2] == 20, "PNG stores unpremultiplied RGB samples beside its soft mask");
        }
        using (var renderer = new PdfRenderer())
        using (var result = SKBitmap.Decode(renderer.ExportPng(rgbaDocument, 0, 1)))
            check(Near(result.GetPixel(0, 0), new SKColor(251, 201, 196), 3), "PNG transparency blends correctly over white in the PDF renderer");
        var originalObjects = NativeObjectSample.Create(); var imageTarget = PdfImageEditor.Read(originalObjects, 0)[0];
        var duplicated = PdfImageEditor.Duplicate(originalObjects, imageTarget, new PointD(24, 24));
        var occurrences = PdfImageEditor.Read(duplicated, 0);
        check(occurrences.Length == 3 && occurrences[1].Bounds.Center.Distance(imageTarget.Bounds.Center + new PointD(24, 24)) < .001, "native duplication creates a correctly offset drawing occurrence");
        using (var before = Open(originalObjects))
        using (var after = Open(duplicated))
            check(Images(before).Count() == Images(after).Count(), "native image duplication shares encoded samples rather than copying them");
        var disproportioned = PdfImageEditor.SetBounds(document, PdfImageEditor.Read(document, 0)[0], new RectD(10, 10, 90, 90));
        var target = PdfImageEditor.Read(disproportioned, 0)[0];
        var proportional = PdfImageEditor.RestoreAspectRatio(disproportioned, target);
        var restored = PdfImageEditor.Read(proportional, 0)[0];
        check(Math.Abs(restored.Bounds.Width / restored.Bounds.Height - 1.5) < .0001 && restored.Bounds.Center.Distance(target.Bounds.Center) < .001, "restore proportions preserves image center and horizontal size");
        var rotated = PdfImageEditor.Rotate(disproportioned, target, 90);
        var properRotated = PdfImageEditor.RestoreAspectRatio(rotated, PdfImageEditor.Read(rotated, 0)[0]);
        var matrix = PdfImageEditor.Read(properRotated, 0)[0].UnitToPage;
        check(Math.Abs(Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B) / Math.Sqrt(matrix.C * matrix.C + matrix.D * matrix.D) - 1.5) < .001, "restore proportions also works after rotation");
        reject(() => PdfImageEditor.OpenImage([], "bad.png"), "empty image import is rejected");
        reject(() => PdfImageEditor.OpenImage(jpeg[..20], "short.jpg"), "truncated image headers are rejected");
        reject(() => PdfImageEditor.OpenImage(jpeg, "photo.jpg", 0), "image import validates DPI");
        reject(() => PdfImageEditor.Duplicate(document, PdfImageEditor.Read(document, 0)[0], new PointD(double.NaN, 0)), "duplicate rejects nonfinite placement");
        using (var gif = SKImage.FromBitmap(original)?.Encode(SKEncodedImageFormat.Webp, 90))
            if (gif is not null) reject(() => PdfImageEditor.OpenImage(gif.ToArray(), "not-png-or-jpeg.webp"), "image import rejects unadvertised codecs");

        // Exercise dimensions near the input budget without full-resolution
        // managed RGB/alpha staging. Timings are measurements, not pass/fail gates.
        var large = Photo(1600, 1000);
        _ = PdfImageEditor.OpenImage(large, "warm.jpg");
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        var imported = PdfImageEditor.OpenImage(large, "photo.jpg"); watch.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var pdfBytes = PdfDocumentEngine.Save(imported, SKTypeface.Default).Bytes;
        check(pdfBytes.Length < large.Length + 5000, "photo PDF overhead stays bounded without recompressing JPEG samples");
        File.WriteAllText("artifacts/structured/photo-performance.json", JsonSerializer.Serialize(new
        {
            Width = 1600, Height = 1000, JpegBytes = large.Length, PdfBytes = pdfBytes.Length,
            ImportMilliseconds = watch.Elapsed.TotalMilliseconds, ManagedAllocatedBytes = bytes,
            OldRgbAlphaScratchBytes = 1600L * 1000 * 4, NewJpegRgbAlphaScratchBytes = 0,
            Note = "One local import; not an overall app-speed claim. Native decoding and renderer memory are not measured by the CLR counter."
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllBytes("artifacts/engine/photo.jpg", jpeg);
        File.WriteAllBytes("artifacts/engine/photo-exif6.jpg", Exif(jpeg, 6));
        File.WriteAllBytes("artifacts/engine/photo-alpha.png", transparent);
        File.WriteAllBytes("artifacts/structured/photo-passthrough.pdf", PdfDocumentEngine.Save(document, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/structured/photo-alpha.pdf", PdfDocumentEngine.Save(rgbaDocument, SKTypeface.Default).Bytes);
    }

    private static bool Near(SKColor a, SKColor b, int tolerance = 4) => Math.Abs(a.Red - b.Red) <= tolerance && Math.Abs(a.Green - b.Green) <= tolerance && Math.Abs(a.Blue - b.Blue) <= tolerance;
    private static PdfDocument Open(PdfWorkspace workspace) => PdfReader.Open(new MemoryStream(workspace.Sources[0].Bytes), PdfDocumentOpenMode.Modify);
    private static IEnumerable<PdfDictionary> Images(PdfDocument document) => document.Internals.GetAllObjects().OfType<PdfDictionary>().Where(item => item.Elements.GetName("/Subtype") == "/Image");

    internal static byte[] Photo(int width = 120, int height = 80)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        var colors = new[] { new SKColor(220, 30, 20), new SKColor(20, 180, 30), new SKColor(20, 40, 220), new SKColor(220, 180, 20) };
        for (var i = 0; i < 4; i++) { paint.Color = colors[i]; canvas.DrawRect(i % 2 * width / 2f, i / 2 * height / 2f, width / 2f, height / 2f, paint); }
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return encoded.ToArray();
    }

    private static byte[] TransparentPng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(12, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.White); bitmap.SetPixel(0, 0, new SKColor(240, 40, 20, 64)); bitmap.SetPixel(1, 0, new SKColor(10, 20, 30, 255));
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    internal static byte[] Exif(byte[] jpeg, int orientation)
    {
        var app = new byte[] { 0xff, 0xe1, 0, 34, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0 };
        return [..jpeg.AsSpan(0, 2), ..app, ..jpeg.AsSpan(2)];
    }
}
