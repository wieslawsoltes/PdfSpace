using PdfSpace.Core;
using SkiaSharp;
using UglyToad.PdfPig;
using PdfSpace.Rendering.Skia;
namespace PdfSpace.Skia;

/// <summary>Single-thread-affine renderer. Owns all parsed sources and a bounded LRU picture cache.</summary>
public sealed class PdfRenderer : IDisposable
{
    private readonly Dictionary<Guid, PdfDocument> _documents = [];
    private readonly Dictionary<(Guid, int), SKPicture> _pictures = [];
    private readonly LinkedList<(Guid, int)> _lru = [];
    private readonly Dictionary<(Guid, int), LinkedListNode<(Guid, int)>> _nodes = [];
    private readonly LinkedList<Guid> _sourceLru = [];
    public int SourceCacheCapacity { get; init; } = 4;
    public int CachedSourceCount => _documents.Count;
    private bool _disposed;
    public SKTypeface Typeface { get; set; } = SKTypeface.Default;
    public int CacheCapacity { get; init; } = 12;
    public int CachedPictureCount => _pictures.Count;
    private SKPicture Picture(PdfWorkspace workspace, PdfPageState page)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = page.SourceId!.Value; var key = (id, page.SourcePage);
        if (_pictures.TryGetValue(key, out var cached)) { _lru.Remove(_nodes[key]); _lru.AddLast(_nodes[key]); return cached; }
        if (!_documents.TryGetValue(id, out var pdf))
        {
            var source = workspace.Sources.First(s => s.Id == id);
            pdf = PdfDocument.Open(source.PreviewBytes ?? source.Bytes, SkiaRenderingParsingOptions.Instance);
            pdf.AddSkiaPageFactory(); _documents.Add(id, pdf);
        }
        _sourceLru.Remove(id); _sourceLru.AddLast(id);
        while (_documents.Count > Math.Max(1, SourceCacheCapacity))
        { var oldId = _sourceLru.First!.Value; _sourceLru.RemoveFirst(); _documents.Remove(oldId, out var oldSource); oldSource?.Dispose(); }
        var picture = pdf.GetPage<SKPicture>(page.SourcePage);
        _pictures.Add(key, picture); _nodes[key] = _lru.AddLast(key);
        while (_pictures.Count > Math.Max(1, CacheCapacity)) { var first = _lru.First!.Value; _lru.RemoveFirst(); _nodes.Remove(first); _pictures.Remove(first, out var old); old?.Dispose(); }
        return picture;
    }
    public static SKRect Rect(RectD r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    public static SKColor Color(uint argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
    public static void TransformPage(SKCanvas canvas, PdfPageState page) => TransformPage(canvas, page, true);
    /// <summary>Applies the page transform; disable clipping only for editor overlays, not PDF content.</summary>
    public static void TransformPage(SKCanvas canvas, PdfPageState page, bool clip)
    {
        var b = page.VisibleBox;
        switch (page.Rotation)
        {
            case 90: canvas.Translate((float)b.Height, 0); canvas.RotateDegrees(90); break;
            case 180: canvas.Translate((float)b.Width, (float)b.Height); canvas.RotateDegrees(180); break;
            case 270: canvas.Translate(0, (float)b.Width); canvas.RotateDegrees(270); break;
        }
        canvas.Translate((float)-b.X, (float)-b.Y);
        if (clip) canvas.ClipRect(Rect(b));
    }
    public void DrawPage(SKCanvas canvas, PdfWorkspace document, PdfPageState page, bool annotations = true)
    {
        canvas.Save();
        try
        {
            using var paper = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(0, 0, (float)page.DisplayWidth, (float)page.DisplayHeight, paper);
            TransformPage(canvas, page);
            if (page.SourceId is not null) canvas.DrawPicture(Picture(document, page));
            if (annotations) foreach (var annotation in page.Annotations) AnnotationPainter.Draw(canvas, annotation, Typeface);
            foreach (var field in page.Fields) FormFieldPainter.Draw(canvas, field, Typeface);
        }
        finally { canvas.Restore(); }
    }
    /// <summary>Exports a new, flattened visual PDF. Source files and editable workspace data remain unchanged.</summary>
    public byte[] ExportPdf(PdfWorkspace workspace, IEnumerable<int>? selectedPages = null)
    {
        using var stream = new MemoryStream();
        using var pdf = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = workspace.Title, Author = workspace.Author, Creator = "PdfSpace / SkiaSharp", Subject = "Flattened visual export. Preserve the .pdfspace workspace for editable annotations." }) ?? throw new NotSupportedException("PDF export is unavailable in this Skia build.");
        foreach (var i in selectedPages ?? Enumerable.Range(0, workspace.Pages.Length))
        {
            if (i < 0 || i >= workspace.Pages.Length) throw new ArgumentOutOfRangeException(nameof(selectedPages));
            var page = workspace.Pages[i]; var canvas = pdf.BeginPage((float)page.DisplayWidth, (float)page.DisplayHeight); DrawPage(canvas, workspace, page); pdf.EndPage();
        }
        pdf.Close(); return stream.ToArray();
    }
    public byte[] ExportPng(PdfWorkspace workspace, int pageIndex, double scale = 1.5)
    {
        var page = workspace.Pages[pageIndex]; scale = Math.Min(Math.Clamp(scale, .1, 4), 4096 / Math.Max(page.DisplayWidth, page.DisplayHeight));
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, (int)Math.Ceiling(page.DisplayWidth * scale)), Math.Max(1, (int)Math.Ceiling(page.DisplayHeight * scale))));
        surface.Canvas.Scale((float)scale); DrawPage(surface.Canvas, workspace, page);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    public void RetainSources(PdfWorkspace workspace)
    {
        var ids = workspace.Sources.Select(source => source.Id).ToHashSet();
        foreach (var key in _pictures.Keys.Where(key => !ids.Contains(key.Item1)).ToArray())
        { _pictures.Remove(key, out var picture); picture?.Dispose(); if (_nodes.Remove(key, out var node)) _lru.Remove(node); }
        foreach (var id in _documents.Keys.Where(id => !ids.Contains(id)).ToArray())
        { _documents.Remove(id, out var document); _sourceLru.Remove(id); document?.Dispose(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var p in _pictures.Values) p.Dispose(); foreach (var d in _documents.Values) d.Dispose(); _pictures.Clear(); _documents.Clear(); _lru.Clear(); _nodes.Clear(); _sourceLru.Clear();
    }
}
