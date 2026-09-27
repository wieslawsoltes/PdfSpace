using PdfSpace.Core;
namespace PdfSpace.Layout;

public static class PageGeometry
{
    public static PointD ToDisplay(PdfPageState page, PointD point)
    {
        var b = page.VisibleBox; var x = point.X - b.X; var y = point.Y - b.Y;
        return page.Rotation switch { 90 => new(b.Height - y, x), 180 => new(b.Width - x, b.Height - y), 270 => new(y, b.Width - x), _ => new(x, y) };
    }
    public static PointD ToPage(PdfPageState page, PointD display)
    {
        var b = page.VisibleBox; var p = page.Rotation switch { 90 => new PointD(display.Y, b.Height - display.X), 180 => new(b.Width - display.X, b.Height - display.Y), 270 => new(b.Width - display.Y, display.X), _ => display };
        return new(p.X + b.X, p.Y + b.Y);
    }
    public static RectD DisplayBounds(PdfPageState page, RectD bounds)
    {
        var a = ToDisplay(page, new(bounds.X, bounds.Y)); var b = ToDisplay(page, new(bounds.Right, bounds.Bottom)); return RectD.Between(a, b);
    }
}

public enum PageLayoutMode { Continuous, SinglePage, TwoPage }
public readonly record struct PagePlacement(int Index, RectD Bounds)
{
    public PointD ToPage(PdfPageState page, PointD screen, double zoom) => PageGeometry.ToPage(page, new((screen.X - Bounds.X) / zoom, (screen.Y - Bounds.Y) / zoom));
    public PointD ToScreen(PdfPageState page, PointD point, double zoom)
    { var p = PageGeometry.ToDisplay(page, point); return new(Bounds.X + p.X * zoom, Bounds.Y + p.Y * zoom); }
}

public static class PageLayout
{
    public const double Gap = 20;
    public static PagePlacement[] Arrange(IReadOnlyList<PdfPageState> pages, double viewportWidth, double zoom, double scroll, double pan, PageLayoutMode mode = PageLayoutMode.Continuous, int currentPage = 0)
    {
        zoom = Math.Clamp(zoom, .1, 8); var result = new List<PagePlacement>(); double y = Gap - scroll;
        if (mode == PageLayoutMode.SinglePage)
        {
            var i = Math.Clamp(currentPage, 0, pages.Count - 1); var page = pages[i];
            return [new(i, new(Math.Max(Gap, (viewportWidth - page.DisplayWidth * zoom) / 2) + pan, y, page.DisplayWidth * zoom, page.DisplayHeight * zoom))];
        }
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (mode == PageLayoutMode.TwoPage && i + 1 < pages.Count)
            {
                var next = pages[i + 1]; var width = (page.DisplayWidth + next.DisplayWidth) * zoom + Gap;
                var x = Math.Max(Gap, (viewportWidth - width) / 2) + pan;
                result.Add(new(i, new(x, y, page.DisplayWidth * zoom, page.DisplayHeight * zoom)));
                result.Add(new(i + 1, new(x + page.DisplayWidth * zoom + Gap, y, next.DisplayWidth * zoom, next.DisplayHeight * zoom)));
                y += Math.Max(page.DisplayHeight, next.DisplayHeight) * zoom + Gap; i++;
            }
            else
            {
                result.Add(new(i, new(Math.Max(Gap, (viewportWidth - page.DisplayWidth * zoom) / 2) + pan, y, page.DisplayWidth * zoom, page.DisplayHeight * zoom)));
                y += page.DisplayHeight * zoom + Gap;
            }
        }
        return result.ToArray();
    }
}
