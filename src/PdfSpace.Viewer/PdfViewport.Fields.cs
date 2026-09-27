namespace PdfSpace.Viewer;

public sealed partial class PdfViewport
{
    private PdfTextField? _fieldEditor;
    private PdfFormFieldState? _editingField;
    public void BeginFieldText(PdfFormFieldState field)
    {
        if (!field.CanFill || field.Kind != PdfFieldKind.Text) return;
        FinishText(true);
        _editingField = field;
        var placement = Arrange().First(item => item.Index == Session.CurrentPage);
        var bounds = PageGeometry.DisplayBounds(Session.Page, field.Bounds);
        var input = new PdfTextField("Fill " + field.Name)
        {
            Text = field.Value, FontSize = Math.Max(10, field.FontSize * Zoom), MaxLength = field.MaxLength,
            Width = Math.Max(60, bounds.Width * Zoom), MinHeight = Math.Max(30, bounds.Height * Zoom),
            AcceptsReturn = field.Multiline, TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Padding = new Thickness(3), Background = PdfTheme.Brush("#FFFFFF")
        };
        _fieldEditor = input;
        Canvas.SetLeft(input, placement.Bounds.X + bounds.X * Zoom); Canvas.SetTop(input, placement.Bounds.Y + bounds.Y * Zoom);
        void FocusInput() { if (_fieldEditor == input) { input.Focus(FocusState.Programmatic); input.SelectAll(); } }
        input.Loaded += (_, _) => FocusInput();
        input.LostFocus += (_, _) => FinishField(true);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { FinishField(false); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter && !field.Multiline) { FinishField(true); Focus(FocusState.Programmatic); e.Handled = true; }
        };
        _overlay.Children.Add(input); DispatcherQueue.TryEnqueue(FocusInput); Invalidate();
    }
    private void FinishField(bool commit)
    {
        if (_fieldEditor is not { } input) return;
        var field = _editingField; _fieldEditor = null; _editingField = null; _overlay.Children.Remove(input);
        if (commit && field is not null)
        {
            try { Session.SetFieldValue(field.Id, input.Text); }
            catch (Exception ex) { StatusChanged?.Invoke("Form value was not applied: " + ex.Message); }
        }
        Invalidate();
    }
}
