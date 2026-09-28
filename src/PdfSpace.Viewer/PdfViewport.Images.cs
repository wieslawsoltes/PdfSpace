namespace PdfSpace.Viewer;

/// <summary>Host-supplied immutable geometry for native image editing; the viewport does not parse PDF objects.</summary>
public sealed record NativeImageTarget(int Index, RectD Bounds, PointD[] Corners, bool Editable);

public sealed partial class PdfViewport
{
    private NativeImageTarget[] _imageTargets = [];
    private int _selectedImage = -1;
    private RectD _imageInitial;
    private RectD? _imagePreview;
    public int SelectedNativeImage => _selectedImage;
    public event Action<int>? NativeImageSelected;
    public event Action<int, RectD>? NativeImageChanged;
    public event Action<int>? NativeImageDeleteRequested;
    public event Action<RectD>? NativeImageInsertRequested;
    public void SetNativeImages(NativeImageTarget[] targets, int selected = -1)
    {
        _imageTargets = targets; _selectedImage = selected;
        _imagePreview = null; Invalidate();
    }
    public void SelectNativeImage(int index)
    { _selectedImage = index; Invalidate(); }
    private bool PressNativeImage()
    {
        if (Session.Tool == PdfTool.InsertImage) { _gesture = Gesture.ImageInsert; return true; }
        if (Session.Tool != PdfTool.EditImage) return false;
        var selected = _imageTargets.FirstOrDefault(image => image.Index == _selectedImage);
        if (selected is { Editable: true })
        {
            var handle = Array.FindIndex(Handles(selected.Bounds), point => point.Distance(_start) <= 8 / Zoom);
            if (handle >= 0) { _handle = handle; _imageInitial = selected.Bounds; _imagePreview = selected.Bounds; _gesture = Gesture.ImageResize; return true; }
        }
        selected = _imageTargets.Reverse().FirstOrDefault(image => ContainsImage(image, _start));
        _selectedImage = selected?.Index ?? -1;
        NativeImageSelected?.Invoke(_selectedImage);
        if (selected is { Editable: true }) { _imageInitial = selected.Bounds; _imagePreview = selected.Bounds; _gesture = Gesture.ImageMove; }
        Invalidate(); return true;
    }
    private static bool ContainsImage(NativeImageTarget image, PointD point)
    {
        if (!image.Bounds.Contains(point)) return false;
        if (image.Corners.Length != 4) return true;
        var positive = false; var negative = false;
        for (var i = 0; i < 4; i++)
        {
            var a = image.Corners[i]; var b = image.Corners[(i + 1) % 4];
            var cross = (b.X-a.X) * (point.Y-a.Y) - (b.Y-a.Y) * (point.X-a.X);
            positive |= cross > 1e-8; negative |= cross < -1e-8;
        }
        return !(positive && negative);
    }
    private bool MoveNativeImage(PointD world)
    {
        if (_gesture == Gesture.ImageInsert) { _marquee = RectD.Between(_start, world); return true; }
        if (_gesture == Gesture.ImageMove) { _imagePreview = _imageInitial.Translate(world - _start); return true; }
        if (_gesture != Gesture.ImageResize) return false;
        var b = _imageInitial; var left = b.X; var right = b.Right; var top = b.Y; var bottom = b.Bottom;
        if (_handle is 0 or 6 or 7) left = world.X; if (_handle is 2 or 3 or 4) right = world.X;
        if (_handle is 0 or 1 or 2) top = world.Y; if (_handle is 4 or 5 or 6) bottom = world.Y;
        _imagePreview = RectD.Between(new(left, top), new(right, bottom)); return true;
    }
    private void DrawNativeImages(SKCanvas canvas, int pageIndex)
    {
        if (pageIndex != Session.CurrentPage || Session.Tool != PdfTool.EditImage) return;
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(20, 115, 230, 135), StrokeWidth = (float)(1 / Zoom) };
        using var fill = new SKPaint { Color = SKColors.White };
        foreach (var target in _imageTargets)
        {
            var selected = target.Index == _selectedImage; stroke.Color = selected ? new SKColor(20, 115, 230) : new SKColor(20, 115, 230, 100);
            var bounds = selected && _imagePreview is { } preview ? preview : target.Bounds;
            if (target.Corners.Length == 4 && (!selected || _imagePreview is null))
            {
                using var path = new SKPath(); path.MoveTo((float)target.Corners[0].X, (float)target.Corners[0].Y);
                foreach (var corner in target.Corners.Skip(1)) path.LineTo((float)corner.X, (float)corner.Y);
                path.Close(); canvas.DrawPath(path, stroke);
            }
            else canvas.DrawRect(PdfRenderer.Rect(bounds), stroke);
            if (selected) foreach (var point in Handles(bounds))
            {
                var handle = PdfRenderer.Rect(new(point.X - 3 / Zoom, point.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom));
                canvas.DrawRect(handle, fill); canvas.DrawRect(handle, stroke);
            }
        }
    }
}
