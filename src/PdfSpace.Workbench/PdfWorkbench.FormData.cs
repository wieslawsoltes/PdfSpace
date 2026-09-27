namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private async Task ExportFormDataAsync(bool xfdf)
    {
        Viewport.FinishText(true); var document = Session.Document;
        if (document.IsSensitive && !await _dialogs.ConfirmAsync("Export unencrypted form data?", "Field values from this unlocked document will be saved without encryption. Password and certificate fields are excluded. Protect the exported file as sensitive document content.", "Export form data")) return;
        var data = PdfFormData.FromWorkspace(document);
        var bytes = xfdf ? PdfFormDataCodec.WriteXfdf(data) : PdfFormDataCodec.WriteJson(data);
        await _storage.SaveAsync(BaseName(document.Title) + "-form-data." + (xfdf ? "xfdf" : "json"), bytes, xfdf ? "application/vnd.adobe.xfdf" : "application/json");
        ShowStatus($"{data.Fields.Length} logical field values exported as {(xfdf ? "XFDF" : "JSON")}. The form-data file is unencrypted.");
    }

    private async Task ImportFormDataAsync()
    {
        Viewport.FinishText(true); var context = _active;
        if (context.Session.Document.FieldCount == 0) throw new InvalidOperationException("Open a PDF with form fields before importing data.");
        var before = context.Session.Document;
        var file = await _storage.OpenFormDataAsync(); if (file is null) return;
        if (_active != context || !ReferenceEquals(before, context.Session.Document)) throw new InvalidOperationException("The active document changed during file selection. Import the data again.");
        var data = PdfFormDataCodec.Read(file.Bytes, file.Name);
        var plan = context.Session.PrepareFormDataImport(data);
        if (!plan.CanApply) throw new InvalidDataException("No values were imported. " + string.Join("; ", plan.Errors.Take(5)));
        var unknown = plan.UnknownFields.Count == 0 ? "" : "\n\nUnmatched names will be skipped: " + string.Join(", ", plan.UnknownFields.Take(8)) + (plan.UnknownFields.Count > 8 ? "…" : "");
        if (plan.ChangedFieldCount == 0)
        { ShowStatus($"No field changes. {plan.UnchangedFieldCount} unchanged; {plan.UnknownFields.Count} unmatched." + unknown); return; }
        var summary = $"Import {plan.ChangedFieldCount} changed logical field values from {file.Name}? {plan.UnchangedFieldCount} fields are unchanged. Names are matched exactly. This is one undoable operation." + unknown + "\n\nOnly plain field values are imported. Document references, annotations, rich text and scripts are not followed or executed.";
        if (!await _dialogs.ConfirmAsync("Import form data?", summary, "Import values")) return;
        if (_active != context) throw new InvalidOperationException("The active document changed. Import the data again.");
        context.Session.ApplyFormDataImport(plan);
        ShowFormFields();
        ShowStatus($"Imported {plan.ChangedFieldCount} field values; skipped {plan.UnknownFields.Count} unmatched names. Undo restores the whole batch.");
    }

    private static bool MissingRequiredValue(PdfFormFieldState field) => field.Kind is not (PdfFieldKind.Signature or PdfFieldKind.Unsupported) && field.Required &&
        (string.IsNullOrWhiteSpace(field.Value) || field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton && field.Value == "Off");

    private void CheckRequiredFields()
    {
        Viewport.FinishText(true);
        var missing = Session.Document.Pages.SelectMany((page, index) => page.Fields.Where(MissingRequiredValue).Select(field => (Index: index, Field: field)))
            .GroupBy(item => item.Field.GroupName, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        if (missing.Length == 0) { ShowFormFields(); ShowStatus("No missing required values among the supported form fields. This is not script, XFA or certificate validation."); return; }
        UseTool(PdfTool.FillForm); Viewport.Navigate(missing[0].Index); Session.SelectField(missing[0].Field.Id); ShowFormFields();
        ShowStatus($"{missing.Length} required fields need values. First: {missing[0].Field.Name}. Check boxes must be checked; radio groups need a selection.", true);
    }
}
