namespace PdfSpace.Viewer;

public sealed partial class PdfViewport
{
    private ObjectSnapIndex? _objectSnapIndex;
    private RectD _objectSnapPage;
    private ObjectSnapGuide? _objectSnapVertical, _objectSnapHorizontal;
    private double _objectRotation;
    private bool _snapNativeObjectMovement;

    /// <summary>Optional edge/center snapping while moving a native selection. Alt temporarily bypasses it.</summary>
    public bool SnapNativeObjectMovement
    {
        get => _snapNativeObjectMovement;
        set { _snapNativeObjectMovement = value; _objectSnapVertical = null; _objectSnapHorizontal = null; Invalidate(); }
    }
    public int ObjectSnapIndexBuilds { get; private set; }
    public double ObjectRotationPreview => _objectRotation;
    public RectD? NativeObjectPreviewBounds => _objectPreview;
    public ObjectSnapGuide? ObjectVerticalGuide => _objectSnapVertical;
    public ObjectSnapGuide? ObjectHorizontalGuide => _objectSnapHorizontal;
    public PointD? NativeObjectRotationHandle => _objectSelectionEditable && !EditObjectPoints && Session.Tool == PdfTool.EditObject
        ? RotationHandle(ObjectBounds()) : null;
    public event Action<int[], PointD, double>? NativeObjectsRotated;

    private (PointD Edge, PointD Handle) RotationStem(RectD bounds)
    {
        var page = Session.Page;
        var display = PageGeometry.DisplayBounds(page, bounds);
        var origin = Placement(Session.CurrentPage).Bounds;
        var below = origin.Y + display.Y * Zoom < 32;
        var y = below ? display.Bottom : display.Y;
        var offset = below ? 24 / Zoom : -24 / Zoom;
        return (PageGeometry.ToPage(page, new(display.Center.X, y)),
            PageGeometry.ToPage(page, new(display.Center.X, y + offset)));
    }

    private PointD RotationHandle(RectD bounds) => RotationStem(bounds).Handle;

    // Check before page hit testing: the handle can lie in the pasteboard above a page.
    private bool TryBeginObjectRotation(PointD screen, bool bypassHandles)
    {
        // Match resize-handle routing: Ctrl-click selects underlying content.
        // Shift remains available for constrained rotation from pointer-down.
        if (bypassHandles || NativeObjectRotationHandle is not { } handle) return false;
        var page = Session.Page;
        var placement = Placement(Session.CurrentPage);
        if (placement.ToScreen(page, handle, Zoom).Distance(screen) > 8) return false;
        _dragPage = Session.CurrentPage;
        _start = placement.ToPage(page, screen, Zoom);
        _objectInitial = ObjectBounds(); _objectPreview = _objectInitial;
        _objectRotation = 0; _objectToggleOnClick = null;
        _gesture = Gesture.ObjectRotate;
        return true;
    }

    private void MoveObjectRotation(PointD world)
    {
        _objectRotation = world.Distance(_start) * Zoom < 3 ? 0
            : SelectionRotation.DeltaDegrees(_objectInitial.Center, _start, world, ShiftPressed() ? 15 : 0);
    }

    private RectD SnapObjectMovement(PointD world)
    {
        var raw = world - _start;
        var locked = ShiftPressed();
        var delta = SelectionTransform.ConstrainMove(raw, locked);
        var proposed = _objectInitial.Translate(delta);
        _objectSnapVertical = null; _objectSnapHorizontal = null;
        if (!SnapNativeObjectMovement || AltPressed()) return proposed;
        if (_objectSnapIndex is null || _objectSnapPage != Session.Page.VisibleBox)
        {
            // Cache only value geometry, never a document or source buffer. Selection/index changes invalidate it.
            _objectSnapPage = Session.Page.VisibleBox;
            var targets = _objectTargets.Where(t => !_objectSelectionSet.Contains(t.Index)).Select(t => t.Bounds)
                .Prepend(_objectSnapPage).ToArray();
            _objectSnapIndex = new(targets); ObjectSnapIndexBuilds++;
        }
        var horizontal = !locked || Math.Abs(raw.X) >= Math.Abs(raw.Y);
        var vertical = !locked || !horizontal;
        var result = _objectSnapIndex.Snap(proposed, 6 / Zoom, horizontal, vertical);
        _objectSnapVertical = result.VerticalGuide; _objectSnapHorizontal = result.HorizontalGuide;
        return result.Bounds;
    }

    private void ApplyRotationPreview(SKCanvas canvas)
    {
        var center = _objectInitial.Center;
        canvas.Translate((float)center.X, (float)center.Y);
        canvas.RotateDegrees((float)_objectRotation);
        canvas.Translate((float)-center.X, (float)-center.Y);
    }

    private void DrawRotationHandle(SKCanvas canvas, RectD bounds, SKPaint line, SKPaint fill)
    {
        var (edge, handle) = RotationStem(bounds);
        canvas.DrawLine((float)edge.X, (float)edge.Y, (float)handle.X, (float)handle.Y, line);
        canvas.DrawCircle((float)handle.X, (float)handle.Y, (float)(4 / Zoom), fill);
        canvas.DrawCircle((float)handle.X, (float)handle.Y, (float)(4 / Zoom), line);
    }

    private void DrawObjectSnapGuides(SKCanvas canvas)
    {
        if (_objectSnapVertical is null && _objectSnapHorizontal is null) return;
        using var paint = new SKPaint { Color = new SKColor(177, 48, 178), StrokeWidth = (float)(1 / Zoom), IsAntialias = true };
        if (_objectSnapVertical is { } v)
            canvas.DrawLine((float)v.Coordinate, (float)v.Start, (float)v.Coordinate, (float)v.End, paint);
        if (_objectSnapHorizontal is { } h)
            canvas.DrawLine((float)h.Start, (float)h.Coordinate, (float)h.End, (float)h.Coordinate, paint);
    }
}
