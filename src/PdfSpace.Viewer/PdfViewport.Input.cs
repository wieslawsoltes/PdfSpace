using PdfSpace.Documents;
namespace PdfSpace.Viewer;

public sealed partial class PdfViewport
{
    private enum Gesture { None, Pan, Create, Move, Resize, SelectText, Crop, Pinch, ImageMove, ImageResize, ImageInsert, ObjectMove, ObjectResize, ObjectMarquee, ObjectCrop, ObjectInsert, ObjectNode }
    private Gesture _gesture;
    private PointD _start, _screenStart, _startPan;
    private double _startScroll;
    private int _dragPage, _handle;
    private Annotation? _preview, _original;
    private RectD? _marquee;
    private readonly List<PointD> _points = [];
    private readonly Dictionary<uint, PointD> _touches = [];
    private double _pinchDistance, _pinchZoom;
    private PointD _pinchCenter;
    private bool _releasing;
    private static PointD[] Handles(RectD b) => [new(b.X, b.Y), new(b.Center.X, b.Y), new(b.Right, b.Y), new(b.Right, b.Center.Y), new(b.Right, b.Bottom), new(b.Center.X, b.Bottom), new(b.X, b.Bottom), new(b.X, b.Center.Y)];
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas); var screen = new PointD(point.Position.X, point.Position.Y);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            _touches[e.Pointer.PointerId] = screen;
            if (_touches.Count == 2)
            {
                _preview = null; _marquee = null; _gesture = Gesture.Pinch; var pair = _touches.Values.ToArray(); _pinchDistance = pair[0].Distance(pair[1]); _pinchZoom = Zoom; _pinchCenter = new((pair[0].X + pair[1].X) / 2, (pair[0].Y + pair[1].Y) / 2); _canvas.CapturePointer(e.Pointer); e.Handled = true; return;
            }
        }
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;
        FinishText(true); Focus(FocusState.Pointer); _screenStart = screen; _startPan = new(_pan, 0); _startScroll = _scroll; _canvas.CapturePointer(e.Pointer);
        if (Session.Tool == PdfTool.Hand || point.Properties.IsMiddleButtonPressed) { _gesture = Gesture.Pan; e.Handled = true; return; }
        var hit = HitPage(screen); if (hit < 0) { CancelGesture(); return; }
        var placement = Placement(hit); _dragPage = placement.Index; if (Session.CurrentPage != _dragPage) Session.Navigate(_dragPage);
        var page = Session.Page; _start = placement.ToPage(page, screen, Zoom); _points.Clear(); _points.Add(_start); SelectedText = ""; _searchHighlight = null;
        if (PressObjects(e)) { e.Handled = true; return; }
        if (PressNativeImage()) { e.Handled = true; return; }
        if (Session.Tool == PdfTool.FillForm)
        {
            var field = page.Fields.Reverse().FirstOrDefault(item => item.Bounds.Contains(_start));
            var link = page.Annotations.FirstOrDefault(item => item.Kind == AnnotationKind.Link && item.Bounds.Contains(_start));
            CancelGesture();
            if (field is not null) { Session.SelectField(field.Id); FieldRequested?.Invoke(field); }
            else if (link is not null) LinkRequested?.Invoke(link);
            e.Handled = true; return;
        }
        if (Session.Tool == PdfTool.Select)
        {
            if (Session.SelectedAnnotation is { } selected)
            {
                var handles = Handles(selected.Bounds); var handle = Array.FindIndex(handles, p => p.Distance(_start) <= 8 / Zoom);
                if (handle >= 0) { _handle = handle; _original = selected; _preview = selected; _gesture = Gesture.Resize; e.Handled = true; return; }
            }
            var annotation = page.Annotations.Reverse().FirstOrDefault(a => a.Bounds.Inflate(5 / Zoom).Contains(_start));
            Session.Select(annotation?.Id); _original = annotation; _preview = annotation; _gesture = annotation is null ? Gesture.SelectText : Gesture.Move;
        }
        else if (Session.Tool == PdfTool.Text)
        { CancelGesture(); BeginText(new Annotation { Kind = AnnotationKind.Text, Bounds = new(_start.X, _start.Y, Math.Max(20, Math.Min(280, page.Width - _start.X)), 65), FontSize = Session.FontSize, Color = Session.Color }); }
        else if (Session.Tool == PdfTool.Note)
        { CancelGesture(); NoteRequested?.Invoke(_dragPage, _start); }
        else if (Session.Tool is PdfTool.Check or PdfTool.Stamp)
        {
            var annotation = new Annotation { Kind = Session.Tool == PdfTool.Check ? AnnotationKind.Check : AnnotationKind.Stamp, Text = Session.PendingText.Length > 0 ? Session.PendingText : "APPROVED", Bounds = new(_start.X, _start.Y, Session.Tool == PdfTool.Check ? 18 : 145, Session.Tool == PdfTool.Check ? 18 : 35), Color = Session.Color, FontSize = 17 };
            CancelGesture(); Session.AddAnnotation(annotation);
        }
        else if (Session.Tool == PdfTool.Crop) _gesture = Gesture.Crop;
        else
        {
            _gesture = Gesture.Create;
            var kind = Session.Tool switch { PdfTool.Measure => AnnotationKind.Line, PdfTool.Redact => AnnotationKind.RedactionMark, PdfTool.FormText or PdfTool.FormCheckBox or PdfTool.FormChoice => AnnotationKind.Rectangle, _ => Enum.Parse<AnnotationKind>(Session.Tool.ToString()) };
            _preview = new Annotation { Kind = kind, Bounds = new(_start.X, _start.Y, 0, 0), Color = Session.Tool == PdfTool.Highlight ? (Session.Color == 0xFF1473E6 ? 0xFFFFCA28 : Session.Color) : Session.Color, StrokeWidth = Session.Tool == PdfTool.Signature ? Math.Max(2, Session.StrokeWidth) : Session.StrokeWidth, FontSize = Session.FontSize };
        }
        e.Handled = true; Invalidate();
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        var raw = e.GetCurrentPoint(_canvas).Position; var screen = new PointD(raw.X, raw.Y);
        if (_touches.ContainsKey(e.Pointer.PointerId)) _touches[e.Pointer.PointerId] = screen;
        if (_gesture == Gesture.Pinch && _touches.Count >= 2)
        {
            var pair = _touches.Values.Take(2).ToArray(); var center = new PointD((pair[0].X + pair[1].X) / 2, (pair[0].Y + pair[1].Y) / 2);
            ZoomAt(_pinchZoom * pair[0].Distance(pair[1]) / Math.Max(1, _pinchDistance), _pinchCenter);
            _pan += center.X - _pinchCenter.X; _scroll -= center.Y - _pinchCenter.Y; _pinchCenter = center; ClampScroll(); Invalidate(); e.Handled = true; return;
        }
        if (_gesture == Gesture.None) return;
        if (_gesture == Gesture.Pan) { _pan = _startPan.X + screen.X - _screenStart.X; _scroll = _startScroll - (screen.Y - _screenStart.Y); ClampScroll(); UpdateVisiblePage(); Invalidate(); e.Handled = true; return; }
        var page = Session.Document.Pages[_dragPage]; var placement = Placement(_dragPage); var world = placement.ToPage(page, screen, Zoom);
        world = new(Math.Clamp(world.X, 0, page.Width), Math.Clamp(world.Y, 0, page.Height));
        if (MoveObjects(world)) { e.Handled = true; Invalidate(); return; }
        if (MoveNativeImage(world)) { e.Handled = true; Invalidate(); return; }
        if (_gesture == Gesture.Move && _original is not null) _preview = _original.Move(world - _start);
        else if (_gesture == Gesture.Resize && _original is not null)
        {
            var b = _original.Bounds; var left = b.X; var right = b.Right; var top = b.Y; var bottom = b.Bottom;
            if (_handle is 0 or 6 or 7) left = world.X; if (_handle is 2 or 3 or 4) right = world.X;
            if (_handle is 0 or 1 or 2) top = world.Y; if (_handle is 4 or 5 or 6) bottom = world.Y;
            var resized = RectD.Between(new(left, top), new(right, bottom));
            _preview = _original with { Bounds = resized, Points = _original.Points.Select(p => new PointD(resized.X + (p.X - b.X) / Math.Max(.01, b.Width) * resized.Width, resized.Y + (p.Y - b.Y) / Math.Max(.01, b.Height) * resized.Height)).ToArray() };
        }
        else if (_gesture is Gesture.SelectText or Gesture.Crop) _marquee = RectD.Between(_start, world);
        else if (_gesture == Gesture.Create && _preview is not null)
        {
            if (_points[^1].Distance(world) > .4 && _points.Count < 100000) _points.Add(world);
            var bounds = RectD.Between(_start, world);
            if (_preview.Kind is AnnotationKind.Ink or AnnotationKind.Signature) bounds = _points.Select(p => new RectD(p.X, p.Y, 0, 0)).Aggregate(RectD.Union);
            _preview = _preview with { Bounds = bounds, Points = _preview.Kind is AnnotationKind.Ink or AnnotationKind.Signature ? _points.ToArray() : [_start, world] };
        }
        e.Handled = true; Invalidate();
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        _touches.Remove(e.Pointer.PointerId);
        if (_gesture == Gesture.Pinch) { if (_touches.Count == 0) CancelGesture(); e.Handled = true; return; }
        if (ReleaseObjects()) { e.Handled = true; return; }
        var imagePreview = _imagePreview; var imageIndex = _selectedImage;
        var gesture = _gesture; var preview = _preview; var marquee = _marquee; var index = _dragPage; var original = _original;
        CancelGesture();
        try
        {
            if (gesture is Gesture.ImageMove or Gesture.ImageResize && imagePreview is { Width: > .01, Height: > .01 } imageBounds && imageBounds != _imageInitial)
                NativeImageChanged?.Invoke(imageIndex, imageBounds);
            else if (gesture == Gesture.ImageInsert && marquee is { Width: > 1, Height: > 1 } insertion) NativeImageInsertRequested?.Invoke(insertion);
            else if (gesture == Gesture.Create && preview is not null && (preview.Bounds.Width > .5 || preview.Bounds.Height > .5))
            {
                if (Session.Tool is PdfTool.FormText or PdfTool.FormCheckBox or PdfTool.FormChoice)
                {
                    if (preview.Bounds.Width >= 8 && preview.Bounds.Height >= 8) FieldCreated?.Invoke(Session.Tool switch { PdfTool.FormCheckBox => PdfFieldKind.CheckBox, PdfTool.FormChoice => PdfFieldKind.ComboBox, _ => PdfFieldKind.Text }, preview.Bounds);
                }
                else if (Session.Tool == PdfTool.Link) LinkCreated?.Invoke(preview.Bounds);
                else if (Session.Tool == PdfTool.Measure)
                { var length = preview.Points[0].Distance(preview.Points[^1]); StatusChanged?.Invoke($"Distance: {length:F1} pt / {length * 25.4 / 72:F2} mm (page units)"); }
                else if (preview.Kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.Strikeout)
                {
                    var words = PdfReader.Words(Session.Document, Session.Document.Pages[index]).Where(w => preview.Bounds.Intersects(w.Bounds)).ToArray();
                    if (words.Length == 0) Session.AddAnnotation(preview, index);
                    else
                    {
                        var lines = words.GroupBy(w => Math.Round(w.Bounds.Center.Y / 8)).Select(g => preview with { Id = Guid.NewGuid(), Bounds = g.Select(w => w.Bounds).Aggregate(RectD.Union), Text = string.Join(" ", g.Select(w => w.Text)) }).ToArray();
                        var pageId = Session.Document.Pages[index].Id;
                        Session.Execute("Mark selected text", d => d.UpdatePage(pageId, p => p with { Annotations = [..p.Annotations, ..lines] }));
                    }
                }
                else Session.AddAnnotation(preview, index);
            }
            else if (gesture is Gesture.Move or Gesture.Resize && preview is not null && preview != original) Session.UpdateAnnotation(preview.Id, _ => preview, gesture == Gesture.Move ? "Move annotation" : "Resize annotation");
            else if (gesture == Gesture.Crop && marquee is { Width: >= 10, Height: >= 10 } crop) Session.CropPage(crop);
            else if (gesture == Gesture.SelectText && marquee is { } selection)
            {
                SelectedText = string.Join(" ", PdfReader.Words(Session.Document, Session.Page).Where(w => selection.Intersects(w.Bounds)).Select(w => w.Text));
                _searchHighlight = selection; StatusChanged?.Invoke(SelectedText.Length == 0 ? "No selectable text in this area." : $"Selected {SelectedText.Length} characters. Press Ctrl+C to copy.");
            }
        }
        catch (Exception ex) { StatusChanged?.Invoke(ex.Message); }
        Invalidate(); e.Handled = true;
    }
    public void CancelGesture()
    {
        _objectToggleOnClick = null; _objectPreview = null; _objectNode = null; _objectNodePreview = null;
        _gesture = Gesture.None; _imagePreview = null; _preview = null; _original = null; _marquee = null; _points.Clear();
        if (!_releasing) { _releasing = true; _canvas.ReleasePointerCaptures(); _releasing = false; }
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas); var delta = point.Properties.MouseWheelDelta;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) ZoomAt(Zoom * Math.Exp(delta / 800.0), new(point.Position.X, point.Position.Y));
        else if (point.Properties.IsHorizontalMouseWheel || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift)) { _pan -= delta * .7; Invalidate(); }
        else ScrollBy(-delta * .7);
        e.Handled = true;
    }
    private new void DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var position = e.GetPosition(_canvas); var screen = new PointD(position.X, position.Y); var hit = HitPage(screen); if (hit < 0) return;
        var page = Session.Document.Pages[hit]; var point = Placement(hit).ToPage(page, screen, Zoom);
        var text = page.Annotations.Reverse().FirstOrDefault(a => a.Kind == AnnotationKind.Text && a.Bounds.Contains(point));
        if (text is not null) { if (Session.CurrentPage != hit) Session.Navigate(hit); BeginText(text); e.Handled = true; }
    }
    private void Keyboard(object sender, KeyRoutedEventArgs e)
    {
        if (IsEditingText) return;
        if (ObjectKeyboard(e)) { e.Handled = true; return; }
        if (Session.Tool == PdfTool.EditImage && _selectedImage >= 0 && e.Key is VirtualKey.Delete or VirtualKey.Back)
        { NativeImageDeleteRequested?.Invoke(_selectedImage); e.Handled = true; return; }
        if (HandleFormKey(e)) { e.Handled = true; return; }
        if (e.Key == VirtualKey.Escape) { CancelGesture(); Session.Select(null); Invalidate(); e.Handled = true; }
        else if (e.Key is VirtualKey.Delete or VirtualKey.Back) { if (Session.SelectedFieldId is not null) { try { Session.DeleteField(); } catch (InvalidOperationException ex) { StatusChanged?.Invoke(ex.Message); } } else Session.DeleteSelection(); e.Handled = true; }
        else if (e.Key is VirtualKey.PageDown or VirtualKey.PageUp) { Navigate(Session.CurrentPage + (e.Key == VirtualKey.PageDown ? 1 : -1)); e.Handled = true; }
        else if (Session.SelectedAnnotation is { } selected && e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)
        {
            var delta = e.Key switch { VirtualKey.Left => new PointD(-1, 0), VirtualKey.Right => new(1, 0), VirtualKey.Up => new(0, -1), _ => new(0, 1) };
            Session.UpdateAnnotation(selected.Id, a => a.Move(delta), "Nudge annotation"); e.Handled = true;
        }
    }
}
