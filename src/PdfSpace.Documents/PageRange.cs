using System.Globalization;
namespace PdfSpace.Documents;

public static class PageRange
{
    /// <summary>Parses one-based page ranges into ordered zero-based indices. Duplicates are removed.</summary>
    public static int[] Parse(string value, int pageCount)
    {
        if (pageCount < 1) throw new ArgumentOutOfRangeException(nameof(pageCount));
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return Enumerable.Range(0, pageCount).ToArray();
        var result = new List<int>();
        foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start)) throw new FormatException("Use page numbers and ranges, for example 1, 3-5.");
            var end = start;
            if (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out end)) throw new FormatException("Invalid page range.");
            if (start < 1 || end > pageCount || end < start) throw new ArgumentOutOfRangeException(nameof(value), $"Pages must be between 1 and {pageCount}; ranges must be ascending.");
            for (var i = start; i <= end; i++) if (!result.Contains(i - 1)) result.Add(i - 1);
        }
        if (result.Count == 0) throw new FormatException("Select at least one page."); return result.ToArray();
    }
}
