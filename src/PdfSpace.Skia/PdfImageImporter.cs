using PdfSpace.Core;
using PdfSpace.Documents;
using SkiaSharp;
namespace PdfSpace.Skia;

/// <summary>Creates a PDF page from a single decoded image, honoring EXIF orientation. No OCR or imaginary text layer is added.</summary>
public static class PdfImageImporter
{
    public static PdfWorkspace Open(byte[] bytes, string name, int dpi = 150)
    {
        if (dpi is < 72 or > 600 || bytes.Length is < 1 or > 33554432) throw new InvalidDataException("Use an image up to 32 MB and 72–600 DPI.");
        using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported image. Use PNG or JPEG.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg)) throw new InvalidDataException("Only PNG and JPEG images are supported.");
        var info = codec.Info;
        if (codec.FrameCount > 1 || info.Width < 1 || info.Height < 1 || (long)info.Width * info.Height > 16000000) throw new InvalidDataException("Use a single-frame image up to 16 million pixels.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("The image could not be decoded.");
        var swapped = (int)codec.EncodedOrigin >= 5; var w = swapped ? info.Height : info.Width; var h = swapped ? info.Width : info.Height;
        using var output = new MemoryStream(); using var pdf = SKDocument.CreatePdf(output);
        var canvas = pdf.BeginPage(w * 72f / dpi, h * 72f / dpi); canvas.Clear(SKColors.White); canvas.Scale(72f / dpi);
        var matrix = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity
        };
        canvas.Concat(matrix); canvas.DrawBitmap(bitmap, 0, 0); pdf.EndPage(); pdf.Close();
        return PdfReader.Open(output.ToArray(), Path.GetFileNameWithoutExtension(name) + ".pdf");
    }
    public static PdfWorkspace CreateScanExample(SKTypeface typeface)
    {
        using var surface = SKSurface.Create(new SKImageInfo(1200, 1500)); surface.Canvas.Clear(SKColors.White);
        var c = surface.Canvas;
        AnnotationPainter.DrawText(c, "FIELDWORK / SCANNED MEMO", 100, 100, 34, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "Circular materials review", 100, 205, 48, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "Project: North garden renovation", 100, 340, 30, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "Reference: 2048", 100, 405, 30, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "Keep useful materials in circulation.", 100, 540, 30, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "Repair first. Reuse next. Recycle last.", 100, 610, 30, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "Review the recognized text before sharing.", 100, 760, 28, typeface, SKColors.Black);
        AnnotationPainter.DrawText(c, "This example contains pixels, not PDF text.", 100, 825, 28, typeface, SKColors.Black);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Open(data.ToArray(), "Scanned memo.png");
    }
}
