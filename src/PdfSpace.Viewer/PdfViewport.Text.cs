namespace PdfSpace.Viewer;
public sealed partial class PdfViewport
{
    private PdfTextField? _textEditor;
    private Annotation? _editingAnnotation;
    private bool _finishing;
    public void BeginText(Annotation annotation)
    {
        FinishText(true); _editingAnnotation = annotation;
        var placement = Arrange().First(item => item.Index == Session.CurrentPage); var display = PageGeometry.DisplayBounds(Session.Page, annotation.Bounds);
        var top = new PointD(placement.Bounds.X + display.X * Zoom, placement.Bounds.Y + display.Y * Zoom);
        var field = new PdfTextField("Edit annotation text") { Text = annotation.Text, Width = Math.Max(150, Math.Min(500, display.Width * Zoom)), MinHeight = Math.Max(52, display.Height * Zoom), FontSize = Math.Max(10, annotation.FontSize * Zoom), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(2), Background = PdfTheme.Brush("#F8FFFFFF") };
        _textEditor = field;
        void FocusEditor()
        {
            if (!ReferenceEquals(_textEditor, field)) return;
            field.Focus(FocusState.Programmatic); field.SelectAll();
        }
        // A newly created Uno text box cannot activate its native input before it is loaded.
        field.Loaded += (_, _) => FocusEditor();
        field.KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { FinishText(false); args.Handled = true; } };
        field.LostFocus += (_, _) => { if (ReferenceEquals(_textEditor, field)) FinishText(true); };
        Canvas.SetLeft(field, top.X); Canvas.SetTop(field, top.Y); _overlay.Children.Add(field);
        DispatcherQueue.TryEnqueue(FocusEditor);
        StatusChanged?.Invoke("Type your text. Click outside to apply; Escape cancels."); Invalidate();
    }
    public void FinishText(bool commit)
    {
        FinishField(commit);
        if (_textEditor is null || _finishing) return; _finishing = true;
        var field = _textEditor; var annotation = _editingAnnotation; _textEditor = null; _editingAnnotation = null; _overlay.Children.Clear();
        try
        {
            if (commit && annotation is not null && !string.IsNullOrWhiteSpace(field.Text))
            {
                var updated = annotation with { Text = field.Text };
                if (Session.Page.Annotations.Any(item => item.Id == annotation.Id)) Session.UpdateAnnotation(annotation.Id, _ => updated);
                else Session.AddAnnotation(updated);
            }
        }
        finally { _finishing = false; Invalidate(); }
    }
}
