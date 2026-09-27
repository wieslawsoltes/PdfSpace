using Microsoft.UI.Xaml.Automation;
using Uno.WinUI.Graphics2DSK;
namespace PdfSpace.Viewer;

/// <summary>Virtualized thumbnail canvas. Only visible pages enter the shared bounded picture cache.</summary>
public sealed class PdfThumbnailView : UserControl
{
    private sealed class DrawingCanvas : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Paint { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area) => Paint?.Invoke(canvas, area);
    }
    private readonly DrawingCanvas _canvas = new();
    private readonly PdfViewport _viewport;
    private double _scroll;
    private int _pressed = -1;
    public bool OrganizeMode { get; set; }
    public event Action<int>? PageActivated;
    private int Columns => OrganizeMode ? Math.Max(1, (int)(ActualWidth / 195)) : 1;
    private double CellWidth => Math.Max(100, ActualWidth / Columns);
    private double CellHeight => OrganizeMode ? 235 : 180;
    public PdfThumbnailView(PdfViewport viewport)
    {
        _viewport = viewport; Content = _canvas; _canvas.Paint = Paint; IsTabStop = true; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Page thumbnails. Use arrow keys to navigate.");
        PointerWheelChanged += (_, e) => { _scroll = Math.Clamp(_scroll - e.GetCurrentPoint(this).Properties.MouseWheelDelta * .6, 0, Math.Max(0, TotalHeight() - ActualHeight)); Invalidate(); e.Handled = true; };
        PointerPressed += (_, e) => { _pressed = Hit(e.GetCurrentPoint(this).Position); if (_pressed >= 0) { CapturePointer(e.Pointer); Focus(FocusState.Pointer); } };
        PointerReleased += (_, e) =>
        {
            var target = Hit(e.GetCurrentPoint(this).Position); ReleasePointerCaptures();
            if (target >= 0 && _pressed >= 0)
            {
                if (OrganizeMode && target != _pressed) { _viewport.Navigate(_pressed); _viewport.Session.MovePage(target); }
                _viewport.Navigate(target); PageActivated?.Invoke(target); Invalidate();
            }
            _pressed = -1; e.Handled = true;
        };
        KeyDown += (_, e) => { if (e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right) { var delta = e.Key switch { VirtualKey.Up => -Columns, VirtualKey.Down => Columns, VirtualKey.Left => -1, _ => 1 }; _viewport.Navigate(_viewport.Session.CurrentPage + delta); RevealCurrent(); e.Handled = true; } };
        SizeChanged += (_, _) => Invalidate();
    }
    public void Invalidate() => _canvas.Invalidate();
    private double TotalHeight() => Math.Ceiling((double)_viewport.Session.Document.Pages.Length / Columns) * CellHeight + 25;
    private int Hit(Point point)
    {
        var row = (int)((point.Y + _scroll - 20) / CellHeight); var column = Math.Clamp((int)(point.X / CellWidth), 0, Columns - 1); var index = row * Columns + column;
        return point.Y + _scroll >= 20 && index >= 0 && index < _viewport.Session.Document.Pages.Length ? index : -1;
    }
    public void RevealCurrent() { var top = _viewport.Session.CurrentPage / Columns * CellHeight; if (top < _scroll || top + CellHeight > _scroll + ActualHeight) _scroll = Math.Clamp(top - 12, 0, Math.Max(0, TotalHeight() - ActualHeight)); Invalidate(); }
    private void Paint(SKCanvas canvas, Size area)
    {
        canvas.Clear(OrganizeMode ? new SKColor(238, 239, 241) : new SKColor(248, 248, 248)); var pages = _viewport.Session.Document.Pages;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, TotalHeight() - area.Height));
        var first = Math.Max(0, (int)(_scroll / CellHeight) * Columns); var last = Math.Min(pages.Length, ((int)((_scroll + area.Height) / CellHeight) + 2) * Columns);
        for (var i = first; i < last; i++)
        {
            var page = pages[i]; var scale = Math.Min((CellWidth - 46) / page.DisplayWidth, (CellHeight - 45) / page.DisplayHeight); var x = i % Columns * CellWidth + (CellWidth - page.DisplayWidth * scale) / 2; var y = 20 + i / Columns * CellHeight - _scroll;
            using var border = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = i == _viewport.Session.CurrentPage ? 2 : 1, Color = i == _viewport.Session.CurrentPage ? new(20, 115, 230) : new(194, 194, 194) };
            canvas.Save(); canvas.Translate((float)x, (float)y); canvas.Scale((float)scale);
            try { _viewport.Renderer.DrawPage(canvas, _viewport.Session.Document, page); } catch { using var fill = new SKPaint { Color = SKColors.White }; canvas.DrawRect(0, 0, (float)page.DisplayWidth, (float)page.DisplayHeight, fill); }
            canvas.Restore(); canvas.DrawRect(new SKRect((float)x - 2, (float)y - 2, (float)(x + page.DisplayWidth * scale) + 2, (float)(y + page.DisplayHeight * scale) + 2), border);
            AnnotationPainter.DrawText(canvas, (i + 1).ToString(), i % Columns * CellWidth + CellWidth / 2 - 4, y + CellHeight - 34, 11, _viewport.Renderer.Typeface, new(80, 80, 80));
        }
    }
}
