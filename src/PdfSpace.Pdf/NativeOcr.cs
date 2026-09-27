using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Layout;
namespace PdfSpace.Pdf;

internal static class NativeOcr
{
    private const string Key = "/PdfSpaceOCR";
    public static PdfOcrLayer? Read(PdfPage page, double width, double height)
    {
        var metadata = PdfObjects.Dictionary(page.Elements[Key]);
        if (metadata?.Stream is null) return null;
        if (metadata.Stream.Length > 8 * 1024 * 1024) throw new InvalidDataException("OCR metadata exceeds its limit.");
        var bytes = metadata.Stream.UnfilteredValue;
        if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Expanded OCR metadata exceeds its limit.");
        var layer = JsonSerializer.Deserialize(bytes, PdfOcrJsonContext.Default.PdfOcrLayer) ?? throw new InvalidDataException("Invalid OCR metadata.");
        PdfOcrLayer.Validate(layer, width, height); return layer;
    }
    public static void Write(PdfDocument document, PdfPage page, PdfPageState state, SourceGeometry geometry, OcrPdfFont? font)
    {
        if (state.Ocr is not { } layer) return;
        // Only remove PdfSpace-owned streams. Never rewrite third-party invisible
        // text or the scan's original drawing operators as a side effect.
        for (var i = page.Contents.Elements.Count - 1; i >= 0; i--)
        {
            var content = PdfObjects.Dictionary(page.Contents.Elements[i]);
            if (content?.Elements.GetInteger(Key) == 1)
            {
                var resourceName = content.Elements.GetName("/PdfSpaceOCRFont");
                if (resourceName.StartsWith("/PSOCR", StringComparison.Ordinal)) PdfObjects.Dictionary(page.Resources.Elements["/Font"])?.Elements.Remove(resourceName);
                page.Contents.Elements.RemoveAt(i);
            }
        }
        page.Elements.Remove(Key);
        var visible = state.VisibleBox;
        var words = layer.Words.Where(word => word.Bounds.X >= visible.X && word.Bounds.Y >= visible.Y && word.Bounds.Right <= visible.Right + .01 && word.Bounds.Bottom <= visible.Bottom + .01).ToArray();
        if (words.Length > 0 && font is not null)
        {
            var resourceName = "/PSOCR" + Guid.NewGuid().ToString("N");
            PdfObjects.DictionaryValue(page.Resources, "/Font").Elements[resourceName] = font.Font.Reference!;
            var text = new StringBuilder($"q\nBT\n{resourceName} 1 Tf\n3 Tr\n");
            foreach (var word in words)
            {
                var b = word.Bounds;
                var (bottomLeft, bottomRight, topLeft) = layer.Rotation switch
                {
                    90 => (new PointD(b.Right, b.Bottom), new PointD(b.Right, b.Y), new PointD(b.X, b.Bottom)),
                    180 => (new PointD(b.Right, b.Y), new PointD(b.X, b.Y), new PointD(b.Right, b.Bottom)),
                    270 => (new PointD(b.X, b.Y), new PointD(b.X, b.Bottom), new PointD(b.Right, b.Y)),
                    _ => (new PointD(b.X, b.Bottom), new PointD(b.Right, b.Bottom), new PointD(b.X, b.Y))
                };
                var p = geometry.ToPdf(bottomLeft); var right = geometry.ToPdf(bottomRight) - p; var up = geometry.ToPdf(topLeft) - p;
                var span = Math.Max(.01, font.Ascent + font.Descent); var advance = Math.Max(.001, font.Advance(word.Text));
                var origin = new PointD(p.X + up.X * font.Descent / span, p.Y + up.Y * font.Descent / span);
                text.Append($"{PdfObjects.F(right.X / advance)} {PdfObjects.F(right.Y / advance)} {PdfObjects.F(up.X / span)} {PdfObjects.F(up.Y / span)} {PdfObjects.F(origin.X)} {PdfObjects.F(origin.Y)} Tm <{font.Encode(word.Text)}> Tj\n");
            }
            text.Append("ET\nQ\n");
            var content = page.Contents.AppendContent(); content.Elements.SetInteger(Key, 1); content.Elements.SetName("/PdfSpaceOCRFont", resourceName); content.CreateStream(Encoding.ASCII.GetBytes(text.ToString()));
        }
        // On reopen, logical page coordinates include the newly applied crop and
        // rotation. Normalize metadata to those coordinates, just as PDF text is.
        var normalized = layer with
        {
            Rotation = (layer.Rotation - state.Rotation + 360) % 360,
            Words = words.Select(word => word with { Bounds = PageGeometry.DisplayBounds(state, word.Bounds) }).ToArray()
        };
        PdfOcrLayer.Validate(normalized, state.DisplayWidth, state.DisplayHeight);
        var metadata = new PdfDictionary(document); document.Internals.AddObject(metadata);
        metadata.CreateStream(JsonSerializer.SerializeToUtf8Bytes(normalized, PdfOcrJsonContext.Default.PdfOcrLayer)); page.Elements[Key] = metadata.Reference!;
    }
}
