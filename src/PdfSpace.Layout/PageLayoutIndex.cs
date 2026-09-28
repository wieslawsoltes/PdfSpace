using PdfSpace.Core;
namespace PdfSpace.Layout;

/// <summary>Reusable geometry index. O(1) placement/extent and O(log n + visible pages) viewport queries.</summary>
public sealed class PageLayoutIndex
{
    private double[] _widths = [], _heights = [], _prefix = [], _rowHeights = [];
    public int Count => _widths.Length;
    public PageLayoutMode Mode { get; private set; }
    public int BuildCount { get; private set; }
    private int Columns => Mode == PageLayoutMode.TwoPage ? 2 : 1;
    public PageLayoutIndex(IReadOnlyList<PdfPageState> pages, PageLayoutMode mode = PageLayoutMode.Continuous) => Update(pages, mode);
    /// <summary>Call on document changes, never on every frame. Annotation-only changes retain the existing index.</summary>
    public bool Update(IReadOnlyList<PdfPageState> pages, PageLayoutMode mode)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var changed = Count != pages.Count || Mode != mode || BuildCount == 0;
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (!double.IsFinite(page.DisplayWidth) || !double.IsFinite(page.DisplayHeight) || page.DisplayWidth <= 0 || page.DisplayHeight <= 0)
                throw new ArgumentException("Invalid page geometry.", nameof(pages));
            if (i >= Count || _widths[i] != page.DisplayWidth || _heights[i] != page.DisplayHeight) changed = true;
        }
        if (!changed) return false;
        Mode = mode; _widths = pages.Select(page => page.DisplayWidth).ToArray(); _heights = pages.Select(page => page.DisplayHeight).ToArray();
        var rows = (Count + Columns - 1) / Columns; _rowHeights = new double[rows]; _prefix = new double[rows + 1];
        for (var row = 0; row < rows; row++)
        {
            var index = row * Columns; _rowHeights[row] = index + 1 < Count && Columns == 2 ? Math.Max(_heights[index], _heights[index + 1]) : _heights[index];
            _prefix[row + 1] = _prefix[row] + _rowHeights[row];
        }
        BuildCount++; return true;
    }
    private static double Scale(double zoom)
    { if (!double.IsFinite(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom)); return Math.Clamp(zoom, .1, 8); }
    private void ValidateIndex(int index)
    { if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index)); }
    private double RowTop(int row, double zoom) => PageLayout.Gap * (row + 1) + _prefix[row] * zoom;
    public double Top(int index, double zoom)
    { ValidateIndex(index); return Mode == PageLayoutMode.SinglePage ? PageLayout.Gap : RowTop(index / Columns, Scale(zoom)); }
    public double TotalHeight(double zoom, int currentPage = 0)
    {
        zoom = Scale(zoom); if (Count == 0) return 0;
        if (Mode == PageLayoutMode.SinglePage) { ValidateIndex(currentPage); return _heights[currentPage] * zoom + 2 * PageLayout.Gap; }
        return _prefix[^1] * zoom + (_rowHeights.Length + 1) * PageLayout.Gap;
    }
    public PagePlacement Place(int index, double viewportWidth, double zoom, double scroll = 0, double pan = 0)
    {
        ValidateIndex(index); zoom = Scale(zoom);
        var first = index / Columns * Columns;
        var paired = Columns == 2 && first + 1 < Count;
        var width = paired ? (_widths[first] + _widths[first + 1]) * zoom + PageLayout.Gap : _widths[index] * zoom;
        var x = Math.Max(PageLayout.Gap, (viewportWidth - width) / 2) + pan;
        if (paired && index != first) x += _widths[first] * zoom + PageLayout.Gap;
        return new(index, new(x, Top(index, zoom) - scroll, _widths[index] * zoom, _heights[index] * zoom));
    }
    private int FirstRow(double contentY, double zoom)
    {
        var low = 0; var high = _rowHeights.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (RowTop(mid, zoom) + _rowHeights[mid] * zoom < contentY) low = mid + 1; else high = mid;
        }
        return low;
    }
    public IEnumerable<PagePlacement> Visible(double width, double height, double zoom, double scroll, double pan, int currentPage = 0, double overscan = 30)
    {
        zoom = Scale(zoom); if (Count == 0) yield break;
        if (Mode == PageLayoutMode.SinglePage) { yield return Place(currentPage, width, zoom, scroll, pan); yield break; }
        var viewport = new RectD(0, 0, width, height).Inflate(Math.Max(0, overscan));
        for (var row = FirstRow(scroll - overscan, zoom); row < _rowHeights.Length && RowTop(row, zoom) <= scroll + height + overscan; row++)
            for (var column = 0; column < Columns; column++)
            {
                var index = row * Columns + column; if (index >= Count) break;
                var placement = Place(index, width, zoom, scroll, pan);
                if (placement.Bounds.Intersects(viewport)) yield return placement;
            }
    }
    public int HitTest(PointD point, double width, double zoom, double scroll, double pan, int currentPage = 0)
    {
        zoom = Scale(zoom); if (Count == 0) return -1;
        if (Mode == PageLayoutMode.SinglePage) return Place(currentPage, width, zoom, scroll, pan).Bounds.Contains(point) ? currentPage : -1;
        var row = FirstRow(point.Y + scroll, zoom); if (row >= _rowHeights.Length) return -1;
        for (var i = row * Columns; i < Math.Min(Count, (row + 1) * Columns); i++)
            if (Place(i, width, zoom, scroll, pan).Bounds.Contains(point)) return i;
        return -1;
    }
    public int NearestPage(double contentY, double zoom, int currentPage = 0)
    {
        zoom = Scale(zoom); if (Count == 0) return -1;
        if (Mode == PageLayoutMode.SinglePage) { ValidateIndex(currentPage); return currentPage; }
        var row = FirstRow(contentY, zoom); var nearest = 0; var distance = double.PositiveInfinity;
        for (var i = Math.Max(0, row - 1) * Columns; i < Math.Min(Count, (row + 2) * Columns); i++)
        {
            var candidate = Math.Abs(Top(i, zoom) + _heights[i] * zoom / 2 - contentY);
            if (candidate < distance) { distance = candidate; nearest = i; }
        }
        return nearest;
    }
}
