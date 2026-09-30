using System.Globalization;
namespace PdfSpace.Core;

public enum PageLabelMatch { Found, NotFound, Ambiguous }

/// <summary>Immutable navigation index. Owns label strings only, never PDF bytes or workspace snapshots.</summary>
public sealed class PageLabelIndex
{
    private readonly string[] _labels;
    private readonly PdfPageLabel?[] _definitions;
    private readonly Dictionary<string, int> _lookup;
    public int Count => _labels.Length;
    public string this[int pageIndex] => _labels[pageIndex];

    public PageLabelIndex(IReadOnlyList<PdfPageState> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count > 4096) throw new ArgumentException("Page-label index is limited to 4096 pages.", nameof(pages));
        _lookup = new Dictionary<string, int>(pages.Count, StringComparer.Ordinal);
        _labels = new string[pages.Count];
        _definitions = new PdfPageLabel?[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i] ?? throw new ArgumentException("Null page.", nameof(pages));
            _definitions[i] = page.Label;
            var label = page.Label?.Format() ?? (i + 1).ToString(CultureInfo.InvariantCulture);
            _labels[i] = label;
            if (!_lookup.TryAdd(label, i)) _lookup[label] = -1;
        }
    }

    /// <summary>Reuse the index across annotation/geometry-only snapshots. Does not retain page state or source buffers.</summary>
    public bool Matches(IReadOnlyList<PdfPageState> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count != Count) return false;
        for (var i = 0; i < pages.Count; i++)
            if (pages[i] is not { } page || page.Label != _definitions[i]) return false;
        return true;
    }

    /// <summary>Exact labels win; #N means physical page N and =text forces literal label lookup.
    /// Non-numeric hash prefixes remain ordinary labels. Duplicate labels are always ambiguous.</summary>
    public PageLabelMatch Resolve(string text, out int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(text); pageIndex = -1;
        if (text.StartsWith('=')) return Lookup(text.AsSpan(1), out pageIndex);
        if (text.Length > PdfPageLabel.MaximumLabelLength) return PageLabelMatch.NotFound;
        var query = text.AsSpan();
        if (query.Length > 1 && query[0] == '#' && AllDigits(query[1..]))
            return Physical(query[1..], out pageIndex);
        var match = Lookup(query, out pageIndex);
        return match != PageLabelMatch.NotFound ? match : Physical(query, out pageIndex);
    }

    /// <summary>Resolve arbitrary label text without command-prefix or physical-page interpretation.</summary>
    public PageLabelMatch ResolveLabel(string label, out int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Lookup(label.AsSpan(), out pageIndex);
    }

    private PageLabelMatch Lookup(ReadOnlySpan<char> label, out int pageIndex)
    {
        pageIndex = -1;
        if (label.Length > PdfPageLabel.MaximumLabelLength ||
            !_lookup.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(label, out var found))
            return PageLabelMatch.NotFound;
        if (found < 0) return PageLabelMatch.Ambiguous;
        pageIndex = found;
        return PageLabelMatch.Found;
    }

    private PageLabelMatch Physical(ReadOnlySpan<char> text, out int pageIndex)
    {
        pageIndex = -1;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > Count)
            return PageLabelMatch.NotFound;
        pageIndex = number - 1;
        return PageLabelMatch.Found;
    }

    private static bool AllDigits(ReadOnlySpan<char> text)
    {
        foreach (var ch in text) if (ch is < '0' or > '9') return false;
        return true;
    }
}
