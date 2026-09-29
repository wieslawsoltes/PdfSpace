using System.Globalization;

namespace PdfSpace.Pdf;

/// <summary>Structural preflight only. Native fingerprints and permissions are still revalidated by Edit.</summary>
internal static class PdfObjectSelectionValidator
{
    internal static void Validate(IReadOnlyList<PdfPageObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (objects.Count is < 1 or > 1000)
            throw new ArgumentException("Select between 1 and 1,000 objects.", nameof(objects));
        var ordered = new PdfPageObject[objects.Count];
        var childScopes = new HashSet<string>(StringComparer.Ordinal);
        var first = objects[0] ?? throw new ArgumentException("An object selection cannot contain null.", nameof(objects));
        for (var i = 0; i < objects.Count; i++)
        {
            var item = objects[i] ?? throw new ArgumentException("An object selection cannot contain null.", nameof(objects));
            if (item.PageId != first.PageId || item.SourceId != first.SourceId || item.SourceHash != first.SourceHash || item.SourcePage != first.SourcePage)
                throw new ArgumentException("One edit batch belongs to one source page and snapshot.", nameof(objects));
            if (item.ScopePath is null || item.ScopePath.Length > 4096 || item.Start < 0 || item.End < item.Start)
                throw new ArgumentException("Invalid native occurrence range.", nameof(objects));
            ordered[i] = item;
            var start = item.Start.ToString(CultureInfo.InvariantCulture);
            childScopes.Add(item.ScopePath.Length == 0 ? start : item.ScopePath + "/" + start);
        }
        Array.Sort(ordered, OccurrenceComparer.Instance);
        string? previousScope = null;
        var previousEnd = -1;
        foreach (var item in ordered)
        {
            if (item.ScopePath == previousScope)
            {
                if (item.Start <= previousEnd) ThrowOverlap();
            }
            else
            {
                // Only complete slash-delimited ancestors count: scope 1 is not an ancestor of 10.
                var scope = item.ScopePath;
                if (childScopes.Contains(scope)) ThrowOverlap();
                for (var slash = scope.IndexOf('/'); slash >= 0; slash = scope.IndexOf('/', slash + 1))
                    if (childScopes.Contains(scope[..slash])) ThrowOverlap();
                previousScope = scope;
            }
            previousEnd = item.End;
        }
    }

    private static void ThrowOverlap() => throw new ArgumentException("Select independent occurrences, not duplicates, overlapping ranges, or a Form group and its contents.");

    private sealed class OccurrenceComparer : IComparer<PdfPageObject>
    {
        internal static readonly OccurrenceComparer Instance = new();
        public int Compare(PdfPageObject? x, PdfPageObject? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var scope = string.CompareOrdinal(x.ScopePath, y.ScopePath);
            if (scope != 0) return scope;
            var start = x.Start.CompareTo(y.Start);
            return start != 0 ? start : x.End.CompareTo(y.End);
        }
    }
}
