using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Skia;
using SkiaSharp;
namespace PdfSpace.Ocr;

/// <summary>A recognition transaction. No page is modified until every selected page succeeds and its snapshot is still current.</summary>
public sealed class OcrBatch
{
    private readonly PdfWorkspace _snapshot;
    private readonly Dictionary<Guid, PdfOcrLayer> _layers;
    public int RecognizedPages => _layers.Count;
    public int SkippedPages { get; }
    public int WordCount => _layers.Values.Sum(layer => layer.Words.Length);
    private OcrBatch(PdfWorkspace snapshot, Dictionary<Guid, PdfOcrLayer> layers, int skipped)
    { _snapshot = snapshot; _layers = layers; SkippedPages = skipped; }
    public PdfWorkspace Apply(PdfWorkspace current)
    {
        if (!ReferenceEquals(current, _snapshot)) throw new InvalidOperationException("The document changed during recognition. No OCR results were applied; run recognition again.");
        if (_layers.Count == 0) return current;
        var result = current with { Pages = current.Pages.Select(page => _layers.TryGetValue(page.Id, out var layer) ? page with { Ocr = layer } : page).ToArray() };
        WorkspaceJson.Validate(result); return result;
    }
    public static async Task<OcrBatch> RecognizeAsync(PdfWorkspace snapshot, PdfRenderer renderer, IOcrEngine engine, IReadOnlyList<int> pages, string language = "eng", int dpi = 200, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        WorkspaceJson.Validate(snapshot);
        if (language is not ("eng" or "pol" or "deu")) throw new ArgumentException("Choose English, Polish or German.", nameof(language));
        if (dpi is < 100 or > 300) throw new ArgumentOutOfRangeException(nameof(dpi));
        var indices = pages.Distinct().ToArray();
        if (indices.Length is < 1 or > 100 || indices.Any(index => index < 0 || index >= snapshot.Pages.Length)) throw new ArgumentOutOfRangeException(nameof(pages), "Recognize 1–100 existing pages per operation.");
        var layers = new Dictionary<Guid, PdfOcrLayer>(); var skipped = 0;
        using var textReader = new PdfTextReader(snapshot);
        try
        {
            for (var i = 0; i < indices.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = indices[i]; var page = snapshot.Pages[index];
                if (page.Ocr is null && textReader.Words(page).Length > 0) { skipped++; progress?.Report(new(i + 1, indices.Length, index, "Skipped existing text")); continue; }
                // Never recognize annotation text, form values or pending redaction marks.
                // Render only the original source visuals in the displayed crop/orientation.
                progress?.Report(new(i, indices.Length, index, "Rendering scan"));
                await Task.Yield();
                var image = Raster(snapshot, page, renderer, dpi);
                progress?.Report(new(i, indices.Length, index, "Recognizing text"));
                var tsv = await engine.RecognizeTsvAsync(image, language, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                layers.Add(page.Id, TesseractTsv.Decode(tsv, image, page, language, engine.Name));
                progress?.Report(new(i + 1, indices.Length, index, "Recognized"));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(snapshot, layers, skipped);
        }
        finally { await engine.ReleaseAsync(); }
    }
    public static OcrImage Raster(PdfWorkspace workspace, PdfPageState page, PdfRenderer renderer, int dpi)
    {
        if (dpi is < 100 or > 300) throw new ArgumentOutOfRangeException(nameof(dpi));
        var width = Math.Ceiling(page.DisplayWidth * dpi / 72.0); var height = Math.Ceiling(page.DisplayHeight * dpi / 72.0);
        if (!double.IsFinite(width * height) || width < 1 || height < 1 || width > 10000 || height > 10000 || width * height > 16000000) throw new InvalidOperationException("The OCR raster exceeds 16 million pixels. Lower the resolution or crop the page first.");
        using var surface = SKSurface.Create(new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, SKAlphaType.Opaque)) ?? throw new InvalidOperationException("Could not allocate the OCR raster.");
        surface.Canvas.Clear(SKColors.White); surface.Canvas.Scale((float)(width / page.DisplayWidth), (float)(height / page.DisplayHeight));
        renderer.DrawPage(surface.Canvas, workspace, page with { Annotations = [], Fields = [] }, annotations: false);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray(); if (bytes.Length > 32 * 1024 * 1024) throw new InvalidOperationException("OCR image exceeds 32 MB.");
        return new(bytes, (int)width, (int)height, dpi);
    }
}
