using PdfSpace.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Util;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
namespace PdfSpace.Documents;

/// <summary>Single-operation text reader. Reuses each parsed source during multi-page searches and extraction.</summary>
public sealed class PdfTextReader : IDisposable
{
    private readonly PdfWorkspace _workspace;
    private readonly Dictionary<Guid, PdfDocument> _sources = [];
    private readonly Dictionary<(Guid?, int, PdfOcrLayer?), PdfWord[]> _words = [];
    private bool _disposed;
    public PdfTextReader(PdfWorkspace workspace) => _workspace = workspace;
    public PdfWord[] Words(PdfPageState page)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Reviewed OCR replaces only invisible source text. Visible native additions
        // (headers, watermarks or later edits) must remain searchable as well.
        var key = (page.SourceId, page.SourcePage, page.Ocr);
        if (_words.TryGetValue(key, out var cached)) return cached;
        var recognized = page.Ocr?.Words.Select(word => new PdfWord(word.Text, word.Bounds)).ToArray() ?? [];
        if (page.SourceId is not { } id) return recognized;
        if (!_sources.TryGetValue(id, out var document))
        {
            document = PdfDocument.Open(_workspace.Sources.First(source => source.Id == id).Bytes);
            _sources.Add(id, document);
        }
        if (page.SourcePage > document.NumberOfPages) throw new InvalidDataException("A workspace page refers to a source page that does not exist.");
        var native = document.GetPage(page.SourcePage);
        var letters = page.Ocr is null ? native.Letters : native.Letters.Where(letter =>
            letter.RenderingMode is not (TextRenderingMode.Neither or TextRenderingMode.NeitherClip)).ToArray();
        // Keep the established fast extractor for axis-aligned text. Oblique
        // letters need baseline-aware grouping rather than sorting by page Y.
        var extracted = letters.Any(l => l.TextOrientation == TextOrientation.Other)
            ? DefaultWordExtractor.Instance.GetWords(letters.Where(l => l.TextOrientation != TextOrientation.Other).ToArray())
                .Concat(NearestNeighbourWordExtractor.Instance.GetWords(letters.Where(l => l.TextOrientation == TextOrientation.Other).ToArray()))
            : DefaultWordExtractor.Instance.GetWords(letters);
        var visible = extracted.Select(word => new PdfWord(word.Text, new(word.BoundingBox.Left, page.Height - word.BoundingBox.Top, word.BoundingBox.Width, word.BoundingBox.Height)));
        var words = recognized.Concat(visible).ToArray();
        if (_words.Count >= 32) _words.Remove(_words.Keys.First());
        _words[key] = words;
        return words;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var source in _sources.Values) source.Dispose();
        _sources.Clear(); _words.Clear();
    }
}
