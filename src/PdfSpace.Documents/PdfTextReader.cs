using PdfSpace.Core;
using UglyToad.PdfPig;
namespace PdfSpace.Documents;

/// <summary>Single-operation text reader. Reuses each parsed source during multi-page searches and extraction.</summary>
public sealed class PdfTextReader : IDisposable
{
    private readonly PdfWorkspace _workspace;
    private readonly Dictionary<Guid, PdfDocument> _sources = [];
    private readonly Dictionary<(Guid, int), PdfWord[]> _words = [];
    private bool _disposed;
    public PdfTextReader(PdfWorkspace workspace) => _workspace = workspace;
    public PdfWord[] Words(PdfPageState page)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // A reviewed layer replaces this page's recognized word index. Do not
        // combine it with an older hidden OCR stream in the retained source PDF.
        if (page.Ocr is { } ocr) return ocr.Words.Select(word => new PdfWord(word.Text, word.Bounds)).ToArray();
        if (page.SourceId is not { } id) return [];
        var key = (id, page.SourcePage);
        if (_words.TryGetValue(key, out var cached)) return cached;
        if (!_sources.TryGetValue(id, out var document))
        {
            document = PdfDocument.Open(_workspace.Sources.First(source => source.Id == id).Bytes);
            _sources.Add(id, document);
        }
        if (page.SourcePage > document.NumberOfPages) throw new InvalidDataException("A workspace page refers to a source page that does not exist.");
        var words = document.GetPage(page.SourcePage).GetWords().Select(word => new PdfWord(word.Text, new(word.BoundingBox.Left, page.Height - word.BoundingBox.Top, word.BoundingBox.Width, word.BoundingBox.Height))).ToArray();
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
