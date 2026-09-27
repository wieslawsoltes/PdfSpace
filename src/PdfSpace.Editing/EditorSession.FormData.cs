using PdfSpace.Core;

namespace PdfSpace.Editing;

public sealed partial class EditorSession
{
    public FormDataImportPlan PrepareFormDataImport(PdfFormData data)
    {
        ArgumentNullException.ThrowIfNull(data); data.Validate();
        var before = Document;
        var groups = before.Pages.SelectMany(page => page.Fields).GroupBy(field => field.GroupName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknown = new List<string>(); var errors = new List<string>(); var unchanged = 0;
        foreach (var entry in data.Fields)
        {
            if (!groups.TryGetValue(entry.Name, out var group)) { unknown.Add(entry.Name); continue; }
            var field = group[0];
            try
            {
                if (entry.Values.Length > 1) throw new InvalidDataException("Multiple selections are not supported by this field editor.");
                if (entry.Kind is { } kind && kind != field.Kind) throw new InvalidDataException("The imported field type does not match the PDF.");
                if (field.Kind is PdfFieldKind.Signature or PdfFieldKind.Unsupported || group.Any(item => item.Kind != field.Kind))
                    throw new InvalidDataException("Unsupported or inconsistent field group.");
                var value = entry.Values.FirstOrDefault() ?? "";
                if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && value.Length == 0) value = "Off";
                if (group.All(item => item.Value == value)) { unchanged++; continue; }
                if (group.Any(item => !item.CanFill)) throw new InvalidDataException("The field is read-only.");
                foreach (var widget in group) ValidateFieldValues(widget with { Value = value }, group);
                changes.Add(entry.Name, value);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            { errors.Add(entry.Name + ": " + ex.Message); }
        }
        var after = changes.Count == 0 || errors.Count > 0 ? before : before with
        {
            Pages = before.Pages.Select(page => page with
            {
                Fields = page.Fields.Select(field => changes.TryGetValue(field.GroupName, out var value)
                    ? field with { Value = value, Modified = true } : field).ToArray()
            }).ToArray()
        };
        WorkspaceJson.Validate(after);
        return new(before, after, changes.Count, unchanged, unknown.ToArray(), errors.ToArray());
    }

    public void ApplyFormDataImport(FormDataImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(plan.Before, Document)) throw new InvalidOperationException("The document changed after import preview. Preview the form data again.");
        if (!plan.CanApply) throw new InvalidDataException("No values were imported. " + string.Join("; ", plan.Errors.Take(5)));
        Execute("Import form data", _ => plan.After);
    }
}
