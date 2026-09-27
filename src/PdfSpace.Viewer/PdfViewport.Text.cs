namespace PdfSpace.Viewer;
public sealed partial class PdfViewport
{
    private PdfTextField? _textEditor;
    private Annotation? _editingAnnotation;
    private bool _finishing;
    public void BeginText(Annotation annotation)
    {
        FinishText(true); _editingAnnotation = annotation;
        var placement = Arrange().First(p => p.Index == Session.CurrentPage); var display = PageGeometry.DisplayBounds(Session.Page, annotation.Bounds);
        var top = new PointD(placement.Bounds.X + display.X * Zoom, placement.Bounds.Y + display.Y * Zoom);
        _textEditor = new PdfTextField("Edit annotation text") { Text = annotation.Text, Width = Math.Max(150, Math.Min(500, display.Width * Zoom)), MinHeight = Math.Max(52, display.Height * Zoom), FontSize = Math.Max(10, annotation.FontSize * Zoom), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(2), Background = PdfTheme.Brush("#F8FFFFFF") };
        Canvas.SetLeft(_textEditor, top.X); Canvas.SetTop(_textEditor, top.Y); _overlay.Children.Add(_textEditor);
        _textEditor.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { FinishText(false); e.Handled = true; } };
        _textEditor.LostFocus += (_, _) => FinishText(true);
        DispatcherQueue.TryEnqueue(() => { _textEditor?.Focus(FocusState.Programmatic); _textEditor?.SelectAll(); });
        StatusChanged?.Invoke("Type your text. Click outside to apply; Escape cancels."); Invalidate();
    }
    public void FinishText(bool commit)
    {
        if (_textEditor is null || _finishing) return; _finishing = true;
        var field = _textEditor; var annotation = _editingAnnotation; _textEditor = null; _editingAnnotation = null; _overlay.Children.Clear();
        try
        {
            if (commit && annotation is not null && !string.IsNullOrWhiteSpace(field.Text))
            {
                var updated = annotation with { Text = field.Text };
                if (Session.Page.Annotations.Any(a => a.Id == annotation.Id)) Session.UpdateAnnotation(annotation.Id, _ => updated);
                else Session.AddAnnotation(updated);
            }
        }
        finally { _finishing = false; Invalidate(); }
    }
}
