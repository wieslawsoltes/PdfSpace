using System.Globalization;
namespace PdfSpace.Core;

public enum PageLabelMatch { Found, NotFound, Ambiguous }

/// <summary>Immutable navigation index. Owns label strings only, never PDF bytes or workspace snapshots.</summary>
public sealed class PageLabelIndex
{
    private readonly string[] _labels;
    private readonly Dictionary<string, int> _lookup = new(StringComparer.Ordinal);
    public int Count => _labels.Length;
    public string this[int pageIndex] => _labels[pageIndex];

    public PageLabelIndex(IReadOnlyList<PdfPageState> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count > 4096) throw new ArgumentException("Page-label index is limited to 4096 pages.", nameof(pages));
        _labels = new string[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i] ?? throw new ArgumentException("Null page.", nameof(pages));
            var label = page.Label?.Format() ?? (i + 1).ToString(CultureInfo.InvariantCulture);
            _labels[i] = label;
            if (!_lookup.TryAdd(label, i)) _lookup[label] = -1;
        }
    }

    /// <summary>Exact case-sensitive labels win; duplicate labels are ambiguous. #N always means physical page N.</summary>
    public PageLabelMatch Resolve(string text, out int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(text); pageIndex = -1;
        if (text.Length > PdfPageLabel.MaximumLabelLength) return PageLabelMatch.NotFound;
        if (!text.StartsWith('#') && _lookup.TryGetValue(text, out var found))
        {
            if (found < 0) return PageLabelMatch.Ambiguous;
            pageIndex = found; return PageLabelMatch.Found;
        }
        var physical = text.AsSpan();
        if (physical.StartsWith("#", StringComparison.Ordinal)) physical = physical[1..];
        if (!int.TryParse(physical, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > Count)
            return PageLabelMatch.NotFound;
        pageIndex = number - 1; return PageLabelMatch.Found;
    }
}
