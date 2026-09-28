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
    private RectD _objectSelectionBounds;
    private RectD _objectInitial;
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
        _objectTargets = targets;
        _objectLookup = targets.ToDictionary(target => target.Index);
        _objectSelection = [];
        _objectSelectionSet.Clear();
        _objectSelectionBounds = default;
        _objectPreview = null;
        Invalidate();
    }

    public void SelectNativeObjects(IEnumerable<int> indices, bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(indices);
        _objectSelection = indices.Distinct().Where(_objectLookup.ContainsKey).Order().ToArray();
        _objectSelectionSet = new(_objectSelection);
        _objectSelectionBounds = default;
        for (var index = 0; index < _objectSelection.Length; index++)
        {
            var bounds = _objectLookup[_objectSelection[index]].Bounds;
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
        if (EditObjectPoints && _objectSelection.Length == 1)
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

        if (_objectSelection.Length > 0 && !additive)
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

        var hit = _objectTargets.Reverse().FirstOrDefault(t => t.Bounds.Inflate(3 / Zoom).Contains(_start));
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
            SelectNativeObjects(_objectSelectionSet.Contains(hit.Index) ? _objectSelection.Where(i => i != hit.Index) : _objectSelection.Append(hit.Index));
        else if (!_objectSelectionSet.Contains(hit.Index))
            SelectNativeObjects([hit.Index]);
        if (_objectSelection.Length > 0 && _objectSelection.All(index => _objectLookup[index].Editable))
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
            _marquee = RectD.Between(_start, world);
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
                _objectPreview = _objectInitial.Translate(world - _start);
            return true;
        }

        if (_gesture != Gesture.ObjectResize)
            return false;
        var b = _objectInitial;
        var l = b.X;
        var r = b.Right;
        var t = b.Y;
        var bottom = b.Bottom;
        if (_handle is 0 or 6 or 7)
            l = world.X;
        if (_handle is 2 or 3 or 4)
            r = world.X;
        if (_handle is 0 or 1 or 2)
            t = world.Y;
        if (_handle is 4 or 5 or 6)
            bottom = world.Y;
        _objectPreview = RectD.Between(new(l, t), new(r, bottom));
        return true;
    }

    private bool ReleaseObjects()
    {
        var gesture = _gesture;
        if (gesture is not (Gesture.ObjectMove or Gesture.ObjectResize or Gesture.ObjectMarquee or Gesture.ObjectCrop or Gesture.ObjectInsert or Gesture.ObjectNode))
            return false;
        var preview = _objectPreview;
        var rectangle = _marquee;
        var initial = _objectInitial;
        var indices = _objectSelection.ToArray();
        var node = _objectNode;
        var point = _objectNodePreview;
        var ellipse = Session.Tool == PdfTool.ObjectEllipse;
        var marqueeBase = _marqueeBase;
        CancelGesture();
        try
        {
            if (gesture == Gesture.ObjectMarquee && rectangle is { } selection)
                SelectNativeObjects(marqueeBase.Concat(_objectTargets.Where(t => selection.Intersects(t.Bounds)).Select(t => t.Index)));
            else if (gesture == Gesture.ObjectCrop && rectangle is { Width: > 1, Height: > 1 } crop)
                NativeObjectsCropped?.Invoke(crop);
            else if (gesture == Gesture.ObjectInsert && rectangle is { Width: > 1, Height: > 1 } insertion)
                NativeVectorCreated?.Invoke(insertion, ellipse);
            else if (gesture == Gesture.ObjectNode && node is not null && point is { } p && p != node.Position && indices.Length == 1)
                NativeObjectPointChanged?.Invoke(indices[0], node.Node, node.Ordinate, p);
            else if (gesture is Gesture.ObjectMove or Gesture.ObjectResize && preview is { } bounds && bounds != initial)
                NativeObjectsTransformed?.Invoke(indices, initial, bounds);
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

        if (_objectSelection.Length > 0 && args.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
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
        foreach (var item in _objectTargets)
        {
            var selected = _objectSelectionSet.Contains(item.Index);
            line.Color = selected ? new SKColor(20, 115, 230) : new SKColor(20, 115, 230, 65);
            var bounds = item.Bounds;
            if (selected && _objectPreview is { } preview)
            {
                var sx = _objectInitial.Width > 1e-8 ? preview.Width / _objectInitial.Width : 1;
                var sy = _objectInitial.Height > 1e-8 ? preview.Height / _objectInitial.Height : 1;
                bounds = new(preview.X + (bounds.X - _objectInitial.X) * sx, preview.Y + (bounds.Y - _objectInitial.Y) * sy, bounds.Width * sx, bounds.Height * sy);
            }

            canvas.DrawRect(PdfRenderer.Rect(bounds), line);
            if (selected && EditObjectPoints && _objectSelection.Length == 1)
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
            canvas.DrawRect(PdfRenderer.Rect(bounds), line);
            if (!EditObjectPoints)
                foreach (var point in Handles(bounds))
                {
                    var r = PdfRenderer.Rect(new(point.X - 3 / Zoom, point.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom));
                    canvas.DrawRect(r, fill);
                    canvas.DrawRect(r, line);
                }
        }
    }
}
