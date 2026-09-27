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
        if (string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.GroupName)) throw new ArgumentException("Enter a field name.");
        if (Document.Pages.SelectMany(p => p.Fields).Any(f => f.GroupName == field.GroupName))
            throw new InvalidOperationException("Choose a unique field name.");
        ValidateFieldValues(field, [field]);
        Execute("Add form field", document => document.UpdatePage(page.Id, state => state with { Fields = [..state.Fields, field with { Modified = true }] }));
        SelectField(field.Id);
    }
    public void UpdateField(Guid id, Func<PdfFormFieldState, PdfFormFieldState> update)
    {
        var current = Page.Fields.FirstOrDefault(field => field.Id == id);
        if (current is null) return;
        if (current.Kind is PdfFieldKind.Signature or PdfFieldKind.Unsupported) throw new InvalidOperationException("This field type cannot be edited.");
        var next = update(current);
        if (next == current) return;
        if (next.Id != current.Id || next.SourceKey != current.SourceKey || next.Kind != current.Kind || next.GroupName != current.GroupName || next.Name != current.Name || next.ExportValue != current.ExportValue)
            throw new InvalidOperationException("Field identity, type, group and button export value cannot be changed by a properties update.");
        if (current.ReadOnly && next.Value != current.Value) throw new InvalidOperationException("Make a field editable before changing its value.");
        var group = Document.Pages.SelectMany(page => page.Fields).Where(field => field.GroupName == current.GroupName).ToArray();
        if (group.Any(field => field.Kind != current.Kind)) throw new InvalidOperationException("The source field group has inconsistent types.");
        ValidateFieldValues(next, group);
        // Field properties belong to the logical field; bounds belong only to the selected widget.
        Execute("Edit form field", document => document with
        {
            Pages = document.Pages.Select(page => page with
            {
                Fields = page.Fields.Select(field => field.GroupName != current.GroupName ? field : field with
                {
                    Bounds = field.Id == id ? next.Bounds : field.Bounds,
                    Label = next.Label, Value = next.Value, DefaultValue = next.DefaultValue,
                    ReadOnly = next.ReadOnly, Required = next.Required, Multiline = next.Multiline,
                    MaxLength = next.MaxLength, FontSize = next.FontSize, Options = next.Options, Modified = true
                }).ToArray()
            }).ToArray()
        });
    }
    private static void ValidateFieldValues(PdfFormFieldState field, IReadOnlyList<PdfFormFieldState> group)
    {
        if (field.Options.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() != field.Options.Length)
            throw new ArgumentException("Choice export values must be unique.");
        foreach (var value in new[] { field.Value, field.DefaultValue })
        {
            if (field.Kind == PdfFieldKind.Text && field.MaxLength > 0 && value.Length > field.MaxLength)
                throw new ArgumentException($"Current and default text must fit within {field.MaxLength} characters.");
            if (field.Kind is PdfFieldKind.ComboBox or PdfFieldKind.ListBox && value.Length > 0 && !field.Options.Any(option => option.Value == value))
                throw new ArgumentException("Current and default choices must occur in the options list.");
            if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && value != "Off" && !group.Any(item => item.ExportValue == value))
                throw new ArgumentException("Current and default button values must be Off or an existing export value.");
        }
    }
    public void SetFieldValue(Guid id, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var fields = Document.Pages.SelectMany(page => page.Fields).ToArray();
        var field = fields.FirstOrDefault(field => field.Id == id) ?? throw new ArgumentException("Field not found.", nameof(id));
        if (!field.CanFill) throw new InvalidOperationException("This field is read-only or unsupported.");
        var group = fields.Where(item => item.GroupName == field.GroupName).ToArray();
        if (group.Any(item => !item.CanFill || item.Kind != field.Kind)) throw new InvalidOperationException("The field group cannot be filled consistently.");
        ValidateFieldValues(field with { Value = value }, group);
        if (group.All(item => item.Value == value)) return;
        Execute("Fill " + field.Name, document => document with { Pages = document.Pages.Select(page => page with { Fields = page.Fields.Select(item => item.GroupName == field.GroupName ? item with { Value = value, Modified = true } : item).ToArray() }).ToArray() });
    }
    public void DeleteField()
    {
        if (SelectedField is not { } field) return;
        if (field.Kind is PdfFieldKind.Signature or PdfFieldKind.Unsupported) throw new InvalidOperationException("Deleting unsupported or signature fields is not permitted.");
        var remaining = Document.Pages.SelectMany(page => page.Fields).Where(item => item.GroupName == field.GroupName && item.Id != field.Id).ToArray();
        var resetValue = field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && field.Value == field.ExportValue && !remaining.Any(item => item.ExportValue == field.Value);
        var resetDefault = field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && field.DefaultValue == field.ExportValue && !remaining.Any(item => item.ExportValue == field.DefaultValue);
        Execute("Delete form field", document => document with
        {
            Pages = document.Pages.Select(page => page with
            {
                Fields = page.Fields.Where(item => item.Id != field.Id).Select(item => item.GroupName == field.GroupName && (resetValue || resetDefault)
                    ? item with { Value = resetValue ? "Off" : item.Value, DefaultValue = resetDefault ? "Off" : item.DefaultValue, Modified = true } : item).ToArray()
            }).ToArray()
        });
        SelectField(null);
    }
    public void ResetForm()
    {
        var fields = Document.Pages.SelectMany(page => page.Fields).ToArray();
        var changes = fields.Where(field => field.CanFill && field.Value != field.DefaultValue).ToArray();
        if (changes.Length == 0) return;
        foreach (var field in changes) ValidateFieldValues(field with { Value = field.DefaultValue }, fields.Where(item => item.GroupName == field.GroupName).ToArray());
        Execute("Reset form", document => document with { Pages = document.Pages.Select(page => page with { Fields = page.Fields.Select(field => field.CanFill && field.Value != field.DefaultValue ? field with { Value = field.DefaultValue, Modified = true } : field).ToArray() }).ToArray() });
    }
}
