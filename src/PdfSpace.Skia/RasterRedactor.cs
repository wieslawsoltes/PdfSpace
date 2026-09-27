using PdfSpace.Core;
using SkiaSharp;
namespace PdfSpace.Skia;

/// <summary>Destructive redacted-copy exporter. Only freshly rasterized, overwritten pixels reach the output.</summary>
public static class RasterRedactor
{
    public static byte[] Export(PdfWorkspace workspace, PdfRenderer renderer, int dpi = 144, CancellationToken cancellationToken = default)
    {
        if (dpi is < 72 or > 300) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (!workspace.Pages.Any(page => page.Annotations.Any(a => a.Kind == AnnotationKind.RedactionMark)))
            throw new InvalidOperationException("Mark at least one area before applying redactions.");
        using var output = new MemoryStream();
        // Deliberately no source metadata, document catalog, font objects, attachments or source streams.
        using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Creator = "PdfSpace raster redaction", RasterDpi = dpi });
        foreach (var page in workspace.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scale = dpi / 72.0;
            var width = Math.Ceiling(page.DisplayWidth * scale); var height = Math.Ceiling(page.DisplayHeight * scale);
            if (!double.IsFinite(width * height) || width > 12000 || height > 12000 || width * height > 32000000)
                throw new InvalidOperationException("The requested page exceeds the redaction raster limit. Reduce the output resolution; no output was produced.");
            using var surface = SKSurface.Create(new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Opaque)) ?? throw new InvalidOperationException("Raster allocation failed.");
            var canvas = surface.Canvas; canvas.Clear(SKColors.White); canvas.Scale((float)scale);
            var marks = page.Annotations.Where(a => a.Kind == AnnotationKind.RedactionMark).ToArray();
            var clean = page with { Annotations = page.Annotations.Where(a => a.Kind != AnnotationKind.RedactionMark).ToArray() };
            // Fail closed: render exceptions escape. A blank/error placeholder must never count as successful redaction.
            renderer.DrawPage(canvas, workspace, clean);
            canvas.Save(); PdfRenderer.TransformPage(canvas, page);
            using var black = new SKPaint { Color = SKColors.Black, IsAntialias = false, BlendMode = SKBlendMode.Src };
            foreach (var mark in marks) canvas.DrawRect(PdfRenderer.Rect(mark.Bounds.Inflate(1 / scale)), black);
            canvas.Restore(); canvas.Flush();
            using var image = surface.Snapshot();
            var pageCanvas = document.BeginPage((float)page.DisplayWidth, (float)page.DisplayHeight);
            pageCanvas.DrawImage(image, new SKRect(0, 0, (float)page.DisplayWidth, (float)page.DisplayHeight));
            document.EndPage();
            if (output.Length > 256 * 1024 * 1024) throw new InvalidOperationException("Redacted output exceeded 256 MB; no output was produced.");
        }
        document.Close();
        return output.ToArray();
    }
}
