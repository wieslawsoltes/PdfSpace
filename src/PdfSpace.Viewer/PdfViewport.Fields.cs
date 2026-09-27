using Microsoft.UI.Input;
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
        input.LostFocus += (_, _) => { if (_fieldEditor == input) FinishField(true); };
        input.KeyDown += (_, args) =>
        {
            if (HandleFormTab(args)) return;
            if (args.Key == VirtualKey.Escape) { FinishField(false); Focus(FocusState.Programmatic); args.Handled = true; }
            else if (args.Key == VirtualKey.Enter && !field.Multiline) { FinishField(true); Focus(FocusState.Programmatic); args.Handled = true; }
        };
        _overlay.Children.Add(input); DispatcherQueue.TryEnqueue(FocusInput); Invalidate();
    }
    private bool _fieldNavigationPending;
    private bool HandleFormTab(KeyRoutedEventArgs args)
    {
        if (args.Handled || Session.Tool != PdfTool.FillForm || args.Key != VirtualKey.Tab) return false;
        args.Handled = true;
        if (_fieldNavigationPending) return true;
        var backwards = ShiftPressed();
        _fieldNavigationPending = true;
        // Explicit routed-event subscriptions avoid depending on virtual-event
        // override discovery in a trimmed browser runtime. Defer creating the
        // next native editor until the current key dispatch has completed.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _fieldNavigationPending = false;
            if (!_disposed && Session.Tool == PdfTool.FillForm) NavigateField(backwards);
        })) _fieldNavigationPending = false;
        return true;
    }
    /// <summary>Navigate editable widgets in page and widget-array order without activating check boxes.</summary>
    public void NavigateField(bool backwards = false)
    {
        var selected = _editingField?.Id ?? Session.SelectedFieldId;
        FinishText(true);
        var fields = Session.Document.Pages.SelectMany((page, index) => page.Fields.Where(field => field.CanFill).Select(field => (Page: index, Field: field))).ToArray();
        if (fields.Length == 0) { StatusChanged?.Invoke("No editable form fields in this document."); return; }
        var current = Array.FindIndex(fields, item => item.Field.Id == selected);
        var next = current < 0 ? (backwards ? fields.Length - 1 : 0) : (current + (backwards ? -1 : 1) + fields.Length) % fields.Length;
        var target = fields[next];
        Navigate(target.Page);
        Session.SelectField(target.Field.Id);
        var placement = Arrange().First(item => item.Index == target.Page);
        var screen = placement.ToScreen(Session.Page, target.Field.Bounds.Center, Zoom);
        if (screen.Y < 35 || screen.Y > ActualHeight - 35) { _scroll += screen.Y - ActualHeight / 2; ClampScroll(); }
        if (target.Field.Kind == PdfFieldKind.Text) BeginFieldText(target.Field);
        else Focus(FocusState.Programmatic);
        StatusChanged?.Invoke($"Field {next + 1} of {fields.Length}: {target.Field.Name}. Tab moves; Space selects buttons; arrows change choices.");
        Invalidate();
    }
    private static bool ShiftPressed() => (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private bool HandleFormKey(KeyRoutedEventArgs args)
    {
        if (Session.Tool != PdfTool.FillForm) return false;
        if (HandleFormTab(args)) return true;
        if (Session.SelectedField is not { CanFill: true } field) return false;
        if (args.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
                Session.SetFieldValue(field.Id, field.Kind == PdfFieldKind.RadioButton || !field.IsChecked ? field.ExportValue : "Off");
            else if (field.Kind == PdfFieldKind.Text) BeginFieldText(field);
            else return false;
            return true;
        }
        if (args.Key is VirtualKey.Up or VirtualKey.Down && field.Kind is PdfFieldKind.ComboBox or PdfFieldKind.ListBox && field.Options.Length > 0)
        {
            var index = Array.FindIndex(field.Options, option => option.Value == field.Value);
            index = Math.Clamp(index + (args.Key == VirtualKey.Down ? 1 : -1), 0, field.Options.Length - 1);
            Session.SetFieldValue(field.Id, field.Options[index].Value);
            return true;
        }
        return false;
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
