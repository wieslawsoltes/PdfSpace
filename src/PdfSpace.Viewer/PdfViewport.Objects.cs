using Microsoft.UI.Input;

namespace PdfSpace.Viewer;
public sealed record NativeObjectPoint(int Node, int Ordinate, PointD Position);
public sealed record NativeObjectTarget(int Index, string Kind, RectD Bounds, bool Editable, NativeObjectPoint[] Points);
public sealed partial class PdfViewport
{
    private NativeObjectTarget[] _objectTargets = [];
    private int[] _objectSelection = [];
    private Dictionary<int, NativeObjectTarget> _objectLookup = [];
    private HashSet<int> _objectSelectionSet = [];
    private SpatialBoundsIndex _objectSpatial = SpatialBoundsIndex.Empty;
    private Dictionary<int, int> _objectOrdinals = [];
    private readonly List<int> _visibleObjects = [];
    private readonly List<int> _marqueeCandidates = [];
    public int LastObjectHitTestCount { get; private set; }
    public int VisibleObjectOutlineCount { get; private set; }
    private int[] _objectSelectionOrdinals = [];
    private readonly List<int> _drawObjects = [];
    private RectD _objectSelectionBounds;
    private bool _objectSelectionEditable;
    private RectD _objectInitial;
    private int? _objectToggleOnClick;
    private bool _objectDragStarted;
    private RectD? _objectPreview;
    private int[] _marqueeBase = [];
    private NativeObjectPoint? _objectNode;
    private PointD? _objectNodePreview;
    public bool EditObjectPoints { get; set; }
    public int[] SelectedNativeObjects => _objectSelection.ToArray();

    public event Action<int[]>? NativeObjectsSelectionChanged;
    public event Action<int[], RectD, RectD>? NativeObjectsTransformed;
    public event Action<int, int, int, PointD>? NativeObjectPointChanged;
    public event Action<RectD>? NativeObjectsCropped;
    public event Action<RectD, bool>? NativeVectorCreated;
    public event Action<string>? NativeObjectCommand;
    public void SetNativeObjects(NativeObjectTarget[] targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        // Validate the replacement before touching the active selection.
        var copy = targets.ToArray();
        var lookup = copy.ToDictionary(target => target.Index);
        var ordinals = copy.Select((target, ordinal) => (target.Index, ordinal)).ToDictionary(p => p.Index, p => p.ordinal);
        var spatial = new SpatialBoundsIndex(copy.Select(t => t.Bounds).ToArray());
        _objectSnapIndex = null;
        _objectTargets = copy;
        _objectLookup = lookup;
        _objectOrdinals = ordinals;
        _objectSpatial = spatial;
        _visibleObjects.Clear();
        _marqueeCandidates.Clear();
        _objectSelection = [];
        _objectSelectionOrdinals = [];
        _objectSelectionSet.Clear();
        _objectSelectionBounds = default;
        _objectSelectionEditable = false;
        _objectPreview = null;
        Invalidate();
    }

    public void SelectNativeObjects(IEnumerable<int> indices, bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(indices);
        var next = indices.Distinct().Where(_objectLookup.ContainsKey).Order().ToArray();
        if (next.AsSpan().SequenceEqual(_objectSelection)) return;
        _objectSnapIndex = null;
        _objectSelection = next;
        _objectSelectionSet = new(_objectSelection);
        _objectSelectionOrdinals = _objectSelection.Select(index => _objectOrdinals[index]).Order().ToArray();
        _objectSelectionBounds = default;
        _objectSelectionEditable = next.Length > 0;
        for (var index = 0; index < _objectSelection.Length; index++)
        {
            var target = _objectLookup[_objectSelection[index]];
            var bounds = target.Bounds;
            _objectSelectionEditable &= target.Editable;
            _objectSelectionBounds = index == 0 ? bounds : RectD.Union(_objectSelectionBounds, bounds);
        }
        if (notify)
            NativeObjectsSelectionChanged?.Invoke(_objectSelection.ToArray());
        Invalidate();
    }

    // Selection membership and union bounds are computed only when it changes,
    // not N objects × M selected objects on every pointer/render update.
    private RectD ObjectBounds() => _objectSelectionBounds;
    private bool PressObjects(PointerRoutedEventArgs args)
    {
        if (Session.Tool is PdfTool.ObjectRectangle or PdfTool.ObjectEllipse)
        {
            _gesture = Gesture.ObjectInsert;
            return true;
        }

        if (Session.Tool == PdfTool.ObjectCrop)
        {
            _gesture = Gesture.ObjectCrop;
            return true;
        }

        if (Session.Tool != PdfTool.EditObject)
            return false;
        var additive = args.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || args.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        if (EditObjectPoints && _objectSelectionEditable && _objectSelection.Length == 1)
        {
            var selected = _objectLookup.GetValueOrDefault(_objectSelection[0]);
            _objectNode = selected?.Points.FirstOrDefault(p => p.Position.Distance(_start) <= 7 / Zoom);
            if (_objectNode is not null)
            {
                _objectNodePreview = _objectNode.Position;
                _gesture = Gesture.ObjectNode;
                return true;
            }
        }

        if (_objectSelectionEditable && !args.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            var bounds = ObjectBounds();
            var handle = Array.FindIndex(Handles(bounds), p => p.Distance(_start) <= 7 / Zoom);
            if (handle >= 0)
            {
                _handle = handle;
                _objectInitial = bounds;
                _objectPreview = bounds;
                _gesture = Gesture.ObjectResize;
                return true;
            }
        }

        var hitOrdinal = _objectSpatial.HitTest(_start, 3 / Zoom, out var testedBounds);
        LastObjectHitTestCount = testedBounds;
        var hit = hitOrdinal < 0 ? null : _objectTargets[hitOrdinal];
        if (hit is null)
        {
            _marqueeBase = additive ? _objectSelection.ToArray() : [];
            if (!additive)
                SelectNativeObjects([]);
            _gesture = Gesture.ObjectMarquee;
            _marquee = new(_start.X, _start.Y, 0, 0);
            return true;
        }

        if (additive)
        {
            // Shift-click toggles, but Shift-drag must retain an already selected object
            // so the same modifier can constrain its movement.
            if (_objectSelectionSet.Contains(hit.Index))
            {
                if (_objectSelectionEditable) _objectToggleOnClick = hit.Index;
                else SelectNativeObjects(_objectSelection.Where(index => index != hit.Index));
            }
            else SelectNativeObjects(_objectSelection.Append(hit.Index));
        }
        else if (!_objectSelectionSet.Contains(hit.Index))
            SelectNativeObjects([hit.Index]);
        if (_objectSelectionEditable)
        {
            _objectInitial = ObjectBounds();
            _objectPreview = _objectInitial;
            _gesture = Gesture.ObjectMove;
        }

        return true;
    }

    private bool MoveObjects(PointD world)
    {
        if (_gesture is Gesture.ObjectMarquee or Gesture.ObjectCrop or Gesture.ObjectInsert)
        {
            _marquee = _gesture == Gesture.ObjectInsert
                ? SelectionTransform.Create(_start, world, ShiftPressed(), AltPressed())
                : RectD.Between(_start, world);
            return true;
        }

        if (_gesture == Gesture.ObjectNode)
        {
            _objectNodePreview = world;
            return true;
        }

        if (_gesture == Gesture.ObjectMove)
        {
            if (world.Distance(_start) * Zoom >= 3)
            {
                _objectDragStarted = true;
                _objectPreview = SnapObjectMovement(world);
            }
            else
            {
                _objectPreview = _objectInitial;
                _objectSnapVertical = null; _objectSnapHorizontal = null;
            }
            return true;
        }

        if (_gesture != Gesture.ObjectResize)
            return false;
        _objectPreview = SelectionTransform.Resize(_objectInitial, _handle, world - _start, ShiftPressed(), AltPressed());
        return true;
    }

    private bool ReleaseObjects()
    {
        var gesture = _gesture;
        if (gesture is not (Gesture.ObjectMove or Gesture.ObjectResize or Gesture.ObjectMarquee or Gesture.ObjectCrop or Gesture.ObjectInsert or Gesture.ObjectNode or Gesture.ObjectRotate))
            return false;
        var rotation = _objectRotation;
        var preview = _objectPreview;
        var rectangle = _marquee;
        var initial = _objectInitial;
        var indices = _objectSelection.ToArray();
        var node = _objectNode;
        var point = _objectNodePreview;
        var ellipse = Session.Tool == PdfTool.ObjectEllipse;
        var marqueeBase = _marqueeBase;
        var toggle = _objectDragStarted ? null : _objectToggleOnClick;
        CancelGesture();
        try
        {
            if (gesture == Gesture.ObjectMarquee && rectangle is { } selection)
            {
                _objectSpatial.Query(selection, _marqueeCandidates);
                SelectNativeObjects(marqueeBase.Concat(_marqueeCandidates.Select(i => _objectTargets[i].Index)));
            }
            else if (gesture == Gesture.ObjectCrop && rectangle is { Width: > 1, Height: > 1 } crop)
                NativeObjectsCropped?.Invoke(crop);
            else if (gesture == Gesture.ObjectInsert && rectangle is { Width: > 1, Height: > 1 } insertion)
                NativeVectorCreated?.Invoke(insertion, ellipse);
            else if (gesture == Gesture.ObjectNode && node is not null && point is { } p && p != node.Position && indices.Length == 1)
                NativeObjectPointChanged?.Invoke(indices[0], node.Node, node.Ordinate, p);
            else if (gesture == Gesture.ObjectRotate && Math.Abs(rotation) > 1e-7)
                NativeObjectsRotated?.Invoke(indices, initial.Center, rotation);
            else if (gesture is Gesture.ObjectMove or Gesture.ObjectResize && preview is { } bounds && bounds != initial)
                NativeObjectsTransformed?.Invoke(indices, initial, bounds);
            else if (gesture == Gesture.ObjectMove && toggle is { } removed)
                SelectNativeObjects(indices.Where(index => index != removed));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(ex.Message);
        }

        Invalidate();
        return true;
    }

    private bool ObjectKeyboard(KeyRoutedEventArgs args)
    {
        if (Session.Tool != PdfTool.EditObject)
            return false;
        static bool DownKey(VirtualKey k) => (InputKeyboardSource.GetKeyStateForCurrentThread(k) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var command = DownKey(VirtualKey.Control) || DownKey(VirtualKey.LeftWindows) || DownKey(VirtualKey.RightWindows);
        if (command)
        {
            var action = args.Key switch
            {
                VirtualKey.A => "Select all objects",
                VirtualKey.C => "Copy objects",
                VirtualKey.X => "Cut objects",
                VirtualKey.V => "Paste objects",
                VirtualKey.D => "Duplicate objects",
                _ => null
            };
            if (action is null)
                return false;
            NativeObjectCommand?.Invoke(action);
            return true;
        }

        if (args.Key == VirtualKey.Escape)
        {
            CancelGesture();
            SelectNativeObjects([]);
            return true;
        }

        if (args.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            NativeObjectCommand?.Invoke("Delete objects");
            return true;
        }

        if (_objectSelectionEditable && args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var n = DownKey(VirtualKey.Shift) ? 10 : 1;
            var d = args.Key switch
            {
                VirtualKey.Left => new PointD(-n, 0),
                VirtualKey.Right => new(n, 0),
                VirtualKey.Up => new(0, -n),
                _ => new(0, n)};
            var bounds = ObjectBounds();
            NativeObjectsTransformed?.Invoke(_objectSelection, bounds, bounds.Translate(d));
            return true;
        }

        return false;
    }

    private void DrawObjects(SKCanvas canvas, int pageIndex)
    {
        if (pageIndex != Session.CurrentPage || Session.Tool is not (PdfTool.EditObject or PdfTool.ObjectCrop))
            return;
        using var line = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(20, 115, 230, 75),
            StrokeWidth = (float)(1 / Zoom)
        };
        using var fill = new SKPaint
        {
            Color = SKColors.White
        };
        var clip = canvas.LocalClipBounds;
        _objectSpatial.Query(new RectD(clip.Left, clip.Top, Math.Max(0, clip.Width), Math.Max(0, clip.Height)).Inflate(8 / Zoom), _visibleObjects);
        // A selected preview or control point can enter the view even when its
        // original object's bounds lie outside the clip. Keep those candidates.
        var candidates = _visibleObjects;
        if (_objectPreview is not null || EditObjectPoints)
        {
            _drawObjects.Clear();
            var i = 0; var j = 0;
            while (i < _visibleObjects.Count || j < _objectSelectionOrdinals.Length)
            {
                var visible = i < _visibleObjects.Count ? _visibleObjects[i] : int.MaxValue;
                var selected = j < _objectSelectionOrdinals.Length ? _objectSelectionOrdinals[j] : int.MaxValue;
                _drawObjects.Add(Math.Min(visible, selected));
                if (visible <= selected) i++;
                if (selected <= visible) j++;
            }
            candidates = _drawObjects;
        }
        VisibleObjectOutlineCount = candidates.Count;
        foreach (var ordinal in candidates)
        {
            var item = _objectTargets[ordinal];
            var selected = _objectSelectionSet.Contains(item.Index);
            line.Color = selected ? new SKColor(20, 115, 230) : new SKColor(20, 115, 230, 65);
            var bounds = item.Bounds;
            if (selected && _objectPreview is { } preview)
            {
                var sx = _objectInitial.Width > 1e-8 ? preview.Width / _objectInitial.Width : 1;
                var sy = _objectInitial.Height > 1e-8 ? preview.Height / _objectInitial.Height : 1;
                bounds = new(preview.X + (bounds.X - _objectInitial.X) * sx, preview.Y + (bounds.Y - _objectInitial.Y) * sy, bounds.Width * sx, bounds.Height * sy);
            }

            canvas.Save();
            if (selected && _gesture == Gesture.ObjectRotate) ApplyRotationPreview(canvas);
            canvas.DrawRect(PdfRenderer.Rect(bounds), line);
            canvas.Restore();
            if (selected && _objectSelectionEditable && EditObjectPoints && _objectSelection.Length == 1)
                foreach (var point in item.Points)
                {
                    var p = point == _objectNode && _objectNodePreview is { } np ? np : point.Position;
                    var r = PdfRenderer.Rect(new(p.X - 3 / Zoom, p.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom));
                    canvas.DrawRect(r, fill);
                    canvas.DrawRect(r, line);
                }
        }

        if (_objectSelection.Length > 0)
        {
            line.Color = new(20, 115, 230);
            var bounds = _objectPreview ?? ObjectBounds();
            canvas.Save();
            if (_gesture == Gesture.ObjectRotate) ApplyRotationPreview(canvas);
            canvas.DrawRect(PdfRenderer.Rect(bounds), line);
            if (!EditObjectPoints && _objectSelectionEditable)
                foreach (var point in Handles(bounds))
                {
                    var r = PdfRenderer.Rect(new(point.X - 3 / Zoom, point.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom));
                    canvas.DrawRect(r, fill);
                    canvas.DrawRect(r, line);
                }
            if (!EditObjectPoints && _objectSelectionEditable && Session.Tool == PdfTool.EditObject)
                DrawRotationHandle(canvas, bounds, line, fill);
            canvas.Restore();
        }
        DrawObjectSnapGuides(canvas);
    }
}
