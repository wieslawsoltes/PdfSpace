using PdfSpace.Core;
namespace PdfSpace.Documents;

/// <summary>Bounded per-document text index. Annotations are read from the current snapshot, not cached.</summary>
public sealed class PdfSearchIndex
{
    private sealed record Entry(PdfWord[] Words, string Text, int[] Offsets, long Cost);
    private readonly Dictionary<(Guid? Source, int Page, PdfOcrLayer? Ocr), (Entry Entry, LinkedListNode<(Guid?, int, PdfOcrLayer?)> Node)> _pages = [];
    private readonly LinkedList<(Guid?, int, PdfOcrLayer?)> _lru = [];
    private long _cost;
    public long MaximumBytes { get; }
    public long CachedBytes => _cost;
    public int CachedPages => _pages.Count;
    public long PagesIndexed { get; private set; }
    public PdfSearchIndex(long maximumBytes = 16 * 1024 * 1024)
    { if (maximumBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes)); MaximumBytes = maximumBytes; }
    public void Clear() { _pages.Clear(); _lru.Clear(); _cost = 0; }
    public IEnumerable<SearchResult> Find(PdfWorkspace document, string query, bool matchCase = false, bool wholeWord = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        if (query.Length > 4096) throw new ArgumentException("Search query is limited to 4096 characters.", nameof(query));
        cancellationToken.ThrowIfCancellationRequested();
        var sourceIds = document.Sources.Select(source => source.Id).ToHashSet();
        var layers = document.Pages.Where(page => page.Ocr is not null).Select(page => page.Ocr).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var key in _pages.Keys.Where(key => key.Source is { } id && !sourceIds.Contains(id) || key.Ocr is not null && !layers.Contains(key.Ocr)).ToArray()) Remove(key);
        using var reader = new PdfTextReader(document);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var pageIndex = 0; pageIndex < document.Pages.Length; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.Pages[pageIndex]; var key = (page.SourceId, page.SourcePage, page.Ocr);
            Entry entry;
            if (_pages.TryGetValue(key, out var cached)) { entry = cached.Entry; _lru.Remove(cached.Node); _lru.AddLast(cached.Node); }
            else
            {
                var words = reader.Words(page); var text = string.Join(" ", words.Select(word => word.Text));
                var offsets = new int[words.Length]; var position = 0;
                for (var i = 0; i < words.Length; i++) { offsets[i] = position; position += words[i].Text.Length + 1; }
                var cost = (long)text.Length * 4 + (long)words.Length * 80;
                entry = new(words, text, offsets, cost); PagesIndexed++;
                if (cost <= MaximumBytes)
                {
                    while (_lru.First is not null && (_cost + cost > MaximumBytes || _pages.Count >= 4096)) Remove(_lru.First.Value);
                    var node = _lru.AddLast(key); _pages.Add(key, (entry, node)); _cost += cost;
                }
            }
            for (var start = 0; start < entry.Text.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = entry.Text.IndexOf(query, start, comparison); if (index < 0) break;
                start = index + Math.Max(1, query.Length);
                if (wholeWord && !Boundary(entry.Text, index, query.Length)) continue;
                // Binary search the first overlapping word instead of scanning every word for every match.
                var low = 0; var high = entry.Words.Length;
                while (low < high) { var mid = low + (high-low)/2; if (entry.Offsets[mid] + entry.Words[mid].Text.Length <= index) low = mid + 1; else high = mid; }
                RectD? bounds = null;
                for (var i = low; i < entry.Words.Length && entry.Offsets[i] < index + query.Length; i++) bounds = bounds is null ? entry.Words[i].Bounds : RectD.Union(bounds.Value, entry.Words[i].Bounds);
                if (bounds is { } rect)
                {
                    var snippetStart = Math.Max(0, index - 28); var count = Math.Min(entry.Text.Length - snippetStart, query.Length + 75);
                    yield return new(pageIndex, entry.Text.Substring(snippetStart, count), rect);
                }
            }
            foreach (var annotation in page.Annotations)
            {
                var index = annotation.Text.IndexOf(query, comparison);
                while (index >= 0 && wholeWord && !Boundary(annotation.Text, index, query.Length)) index = annotation.Text.IndexOf(query, index + 1, comparison);
                if (index >= 0) yield return new(pageIndex, annotation.Text, annotation.Bounds);
            }
        }
    }
    private static bool Boundary(string text, int start, int length) =>
        (start == 0 || !char.IsLetterOrDigit(text[start - 1]) && text[start - 1] != '_') &&
        (start + length == text.Length || !char.IsLetterOrDigit(text[start + length]) && text[start + length] != '_');
    private void Remove((Guid?, int, PdfOcrLayer?) key)
    { if (_pages.Remove(key, out var item)) { _lru.Remove(item.Node); _cost -= item.Entry.Cost; } }
}
