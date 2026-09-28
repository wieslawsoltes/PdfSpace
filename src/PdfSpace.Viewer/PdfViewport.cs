using Microsoft.UI.Xaml.Automation;
using PdfSpace.Documents;
using Uno.WinUI.Graphics2DSK;
namespace PdfSpace.Viewer;

public sealed partial class PdfViewport : UserControl, IDisposable
{
    private sealed class DrawingCanvas : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Paint { get; set; }
        protected override void RenderOverride(SKCanvas canvas, Size area) => Paint?.Invoke(canvas, area);
    }
    private readonly DrawingCanvas _canvas = new();
    private readonly Canvas _overlay = new();
    private readonly EditorSession _session;
    private readonly PageLayoutIndex _layout;
    private bool _disposed;
    private double _scroll, _pan;
    private PageLayoutMode _mode;
    private RectD? _searchHighlight;
    public EditorSession Session => _session;
    public PdfRenderer Renderer { get; } = new();
    public double Zoom { get; private set; } = .88;
    public double Scroll => _scroll;
    public double Pan => _pan;
    public string SelectedText { get; private set; } = "";
    public bool IsEditingText => _textEditor is not null || _fieldEditor is not null;
    public PageLayoutMode LayoutMode => _mode;
    public event Action? ViewChanged;
    public event Action<string>? StatusChanged;
    public event Action<int, PointD>? NoteRequested;
    public event Action<PdfFormFieldState>? FieldRequested;
    public event Action<PdfFieldKind, RectD>? FieldCreated;
    public event Action<RectD>? LinkCreated;
    public event Action<Annotation>? LinkRequested;
    public event Action<Point>? ContextRequested;
    public PdfViewport(EditorSession session)
    {
        _session = session; _layout = new(session.Document.Pages); IsTabStop = true; HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "PDF document canvas"); AutomationProperties.SetAutomationId(this, "PDF document canvas");
        var root = new Grid(); root.Children.Add(_canvas); root.Children.Add(_overlay); Content = root;
        _canvas.Paint = Paint; _canvas.PointerPressed += Pressed; _canvas.PointerMoved += Moved; _canvas.PointerReleased += Released;
        _canvas.PointerCanceled += (_, _) => CancelGesture(); _canvas.PointerCaptureLost += (_, _) => CancelGesture(); _canvas.PointerWheelChanged += Wheel;
        _canvas.DoubleTapped += DoubleTapped;
        _canvas.RightTapped += (_, e) => { ContextRequested?.Invoke(e.GetPosition(this)); e.Handled = true; };
        SizeChanged += (_, _) => { ClampScroll(); Invalidate(); };
        PreviewKeyDown += (_, args) => HandleFormTab(args);
        KeyDown += Keyboard;
        session.Changed += DocumentChanged; session.ViewChanged += SessionViewChanged;
    }
    private void DocumentChanged(object? sender, EventArgs e) { CancelGesture(); _layout.Update(Session.Document.Pages, _mode); Renderer.RetainSources(Session.Document); ClampScroll(); Invalidate(); }
    private void SessionViewChanged(object? sender, EventArgs e) => Invalidate();
    public void Invalidate() { _canvas.Invalidate(); ViewChanged?.Invoke(); }
    private PagePlacement Placement(int index, double? scroll = null) => _layout.Place(index, ActualWidth, Zoom, scroll ?? _scroll, _pan);
    private int HitPage(PointD screen) => _layout.HitTest(screen, ActualWidth, Zoom, _scroll, _pan, Session.CurrentPage);
    private void ClampScroll()
    {
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _layout.TotalHeight(Zoom, Session.CurrentPage) - ActualHeight));
        _pan = Math.Clamp(_pan, -100000, 100000);
    }
    public void Navigate(int index)
    {
        FinishText(true); Session.Navigate(index); _searchHighlight = null;
        _scroll = _mode == PageLayoutMode.SinglePage ? 0 : _layout.Top(Session.CurrentPage, Zoom) - PageLayout.Gap;
        ClampScroll(); Invalidate();
    }
    public void SetLayout(PageLayoutMode mode) { _mode = mode; _layout.Update(Session.Document.Pages, mode); Navigate(Session.CurrentPage); }
    public void FitPage(bool widthOnly = false)
    {
        var page = Session.Page; var width = Math.Max(100, ActualWidth - 100); var height = Math.Max(100, ActualHeight - 40);
        var fit = width / page.DisplayWidth;
        if (_mode == PageLayoutMode.TwoPage) fit = (width - PageLayout.Gap) / (page.DisplayWidth * 2);
        Zoom = Math.Clamp(widthOnly ? fit : Math.Min(fit, height / page.DisplayHeight), .1, 8); _pan = 0; Navigate(Session.CurrentPage);
    }
    public void ZoomTo(double value) => ZoomAt(value, new(ActualWidth / 2, ActualHeight / 2));
    private void ZoomAt(double value, PointD screen)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        FinishText(true); var hit = HitPage(screen); var index = hit < 0 ? Session.CurrentPage : hit;
        var placement = Placement(index);
        var page = Session.Document.Pages[index]; var anchor = placement.ToPage(page, screen, Zoom);
        Zoom = Math.Clamp(value, .1, 8);
        var after = Placement(index); var location = after.ToScreen(page, anchor, Zoom);
        _pan += screen.X - location.X; _scroll += location.Y - screen.Y; ClampScroll(); Invalidate();
    }
    public void ScrollBy(double delta) { FinishText(true); _scroll += delta; ClampScroll(); UpdateVisiblePage(); Invalidate(); }
    private void UpdateVisiblePage()
    {
        if (_mode == PageLayoutMode.SinglePage) return;
        var index = _layout.NearestPage(_scroll + ActualHeight * .42, Zoom, Session.CurrentPage);
        if (index >= 0 && index != Session.CurrentPage) Session.Navigate(index);
    }
    public void HighlightSearch(SearchResult result)
    { Navigate(result.PageIndex); _searchHighlight = result.Bounds; var placement = Placement(result.PageIndex); var screen = placement.ToScreen(Session.Page, result.Bounds.Center, Zoom); _scroll += screen.Y - ActualHeight * .42; ClampScroll(); Invalidate(); }
    public RectD PageScreenBounds(int index) => Placement(index).Bounds;
    private void Paint(SKCanvas canvas, Size size)
    {
        canvas.Clear(new SKColor(232, 233, 235));
        using var shadow = new SKPaint { Color = new(0, 0, 0, 23) }; using var outline = new SKPaint { Color = new(0, 0, 0, 30), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        foreach (var placement in _layout.Visible(size.Width, size.Height, Zoom, _scroll, _pan, Session.CurrentPage))
        {
            var rect = placement.Bounds; if (!rect.Intersects(new RectD(0, 0, size.Width, size.Height).Inflate(30))) continue;
            var page = Session.Document.Pages[placement.Index];
            canvas.DrawRect(PdfRenderer.Rect(rect.Translate(new(2, 3))), shadow);
            canvas.Save(); canvas.Translate((float)rect.X, (float)rect.Y); canvas.Scale((float)Zoom);
            try
            {
                var displayed = _original is not null && _preview is not null && _dragPage == placement.Index ? page with { Annotations = page.Annotations.Where(annotation => annotation.Id != _original.Id).ToArray() } : page;
                Renderer.DrawPage(canvas, Session.Document, displayed);
                canvas.Save(); PdfRenderer.TransformPage(canvas, page);
                DrawOverlays(canvas, page, placement.Index); canvas.Restore();
            }
            catch (Exception ex)
            {
                using var white = new SKPaint { Color = SKColors.White }; canvas.DrawRect(0, 0, (float)page.DisplayWidth, (float)page.DisplayHeight, white);
                AnnotationPainter.DrawText(canvas, "This page could not be rendered.\n" + ex.Message, 28, 40, 13, Renderer.Typeface, new SKColor(170, 40, 40), page.DisplayWidth - 56);
            }
            finally { canvas.Restore(); }
            canvas.DrawRect(PdfRenderer.Rect(rect), outline);
        }
        if (_scroll > 0 || _layout.TotalHeight(Zoom, Session.CurrentPage) > size.Height)
        {
            var total = Math.Max(size.Height, _layout.TotalHeight(Zoom, Session.CurrentPage)); var height = Math.Max(32, size.Height * size.Height / total);
            using var scrollbar = new SKPaint { IsAntialias = true, Color = new(90, 90, 90, 100) };
            canvas.DrawRoundRect(new SKRect((float)size.Width - 9, (float)(_scroll / total * size.Height), (float)size.Width - 3, (float)(_scroll / total * size.Height + height)), 3, 3, scrollbar);
        }
    }
    private void DrawOverlays(SKCanvas canvas, PdfPageState page, int index)
    {
        DrawNativeImages(canvas, index);
        using var fill = new SKPaint { Color = new(20, 115, 230, 40) }; using var line = new SKPaint { Color = new(20, 115, 230), Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
        if (index == Session.CurrentPage && _searchHighlight is { } found) { fill.Color = new(255, 188, 0, 90); canvas.DrawRect(PdfRenderer.Rect(found.Inflate(2)), fill); }
        if (_dragPage == index && _preview is { } preview) AnnotationPainter.Draw(canvas, preview, Renderer.Typeface);
        if (_dragPage == index && _marquee is { } marquee) { canvas.DrawRect(PdfRenderer.Rect(marquee), fill); canvas.DrawRect(PdfRenderer.Rect(marquee), line); }
        if (Session.Tool is PdfTool.FillForm or PdfTool.FormText or PdfTool.FormCheckBox or PdfTool.FormChoice)
            foreach (var field in page.Fields)
            {
                fill.Color = new SKColor(77, 125, 240, 26); canvas.DrawRect(PdfRenderer.Rect(field.Bounds), fill);
                if (field.Id == Session.SelectedFieldId) canvas.DrawRect(PdfRenderer.Rect(field.Bounds.Inflate(2 / Zoom)), line);
            }
        DrawObjects(canvas, index);
        if (index != Session.CurrentPage) return;
        if (Session.SelectedAnnotation is { } selected && _textEditor is null)
        {
            var bounds = _preview?.Id == selected.Id ? _preview.Bounds : selected.Bounds;
            canvas.DrawRect(PdfRenderer.Rect(bounds.Inflate(2 / Zoom)), line);
            foreach (var point in Handles(bounds))
            { var r = new RectD(point.X - 3 / Zoom, point.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom); fill.Color = SKColors.White; canvas.DrawRect(PdfRenderer.Rect(r), fill); canvas.DrawRect(PdfRenderer.Rect(r), line); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; FinishText(false); Session.Changed -= DocumentChanged; Session.ViewChanged -= SessionViewChanged; Renderer.Dispose(); _canvas.Paint = null;
    }
}
