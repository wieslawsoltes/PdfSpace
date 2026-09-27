using PdfSpace.Core;
namespace PdfSpace.Editing;

public sealed partial class EditorSession
{
    public Guid? SelectedFieldId { get; private set; }
    public PdfFormFieldState? SelectedField => Page.Fields.FirstOrDefault(item => item.Id == SelectedFieldId);
    public void SelectField(Guid? id)
    {
        SelectedAnnotationId = null;
        SelectedFieldId = id;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    public void AddField(PdfFormFieldState field)
    {
        var page = Page;
        if (Document.Pages.SelectMany(p => p.Fields).Any(f => f.GroupName == field.GroupName))
            throw new InvalidOperationException("Choose a unique field name.");
        Execute("Add form field", document => document.UpdatePage(page.Id, state => state with { Fields = [..state.Fields, field with { Modified = true }] }));
        SelectField(field.Id);
    }
    public void UpdateField(Guid id, Func<PdfFormFieldState, PdfFormFieldState> update)
    {
        var page = Page;
        var current = page.Fields.FirstOrDefault(field => field.Id == id);
        if (current is null) return;
        var next = update(current);
        if (next == current) return;
        if (next.Id != current.Id || next.SourceKey != current.SourceKey) throw new InvalidOperationException("Field identities cannot be changed.");
        Execute("Edit form field", document => document.UpdatePage(page.Id, state => state with { Fields = state.Fields.Select(field => field.Id == id ? next with { Modified = true } : field).ToArray() }));
    }
    public void SetFieldValue(Guid id, string value)
    {
        var field = Document.Pages.SelectMany(page => page.Fields).FirstOrDefault(field => field.Id == id) ?? throw new ArgumentException("Field not found.", nameof(id));
        if (!field.CanFill) throw new InvalidOperationException("This field is read-only or unsupported.");
        if (field.MaxLength > 0 && value.Length > field.MaxLength) throw new ArgumentException($"This field accepts at most {field.MaxLength} characters.");
        if (field.Kind is PdfFieldKind.ComboBox or PdfFieldKind.ListBox && field.Options.Length > 0 && !field.Options.Any(option => option.Value == value)) throw new ArgumentException("Choose one of the field's available values.");
        if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && value != "Off" && !Document.Pages.SelectMany(p => p.Fields).Any(f => f.GroupName == field.GroupName && f.ExportValue == value)) throw new ArgumentException("Invalid button state.");
        if (field.Value == value) return;
        Execute("Fill " + field.Name, document => document with { Pages = document.Pages.Select(page => page with { Fields = page.Fields.Select(item => item.GroupName == field.GroupName ? item with { Value = value, Modified = true } : item).ToArray() }).ToArray() });
    }
    public void DeleteField()
    {
        if (SelectedFieldId is not { } id) return;
        var field = SelectedField;
        if (field?.SourceKey is not null) throw new InvalidOperationException("Deleting imported form fields is not supported. Disable the field or use a flattened output.");
        Execute("Delete form field", document => document.UpdatePage(Page.Id, page => page with { Fields = page.Fields.Where(item => item.Id != id).ToArray() }));
        SelectField(null);
    }
    public void ResetForm() => Execute("Reset form", document => document with { Pages = document.Pages.Select(page => page with { Fields = page.Fields.Select(field => field.CanFill ? field with { Value = field.DefaultValue, Modified = true } : field).ToArray() }).ToArray() });
}
