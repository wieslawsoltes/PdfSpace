using System.Globalization;
using System.Text;
using System.Text.Json;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private PdfTextRun[] _originalRuns = [];
    private bool _permitPrint = true, _permitCopy = true, _permitEdit = true;
    private void BuildFormTools()
    {
        _leftPanel.Description("Create native AcroForm fields directly on the page. Existing supported fields can be filled without flattening their values.");
        _leftPanel.Add("Fill form", PdfIconKind.Select, () => { UseTool(PdfTool.FillForm); ShowFormFields(); });
        _leftPanel.Add("Add text field", PdfIconKind.Text, () => UseTool(PdfTool.FormText));
        _leftPanel.Add("Add check box", PdfIconKind.Check, () => UseTool(PdfTool.FormCheckBox));
        _leftPanel.Add("Add dropdown", PdfIconKind.Down, () => UseTool(PdfTool.FormChoice));
        _leftPanel.Add("Show form fields", PdfIconKind.Grid, ShowFormFields);
        _leftPanel.Add("Next field", PdfIconKind.Right, () => Viewport.NavigateField());
        _leftPanel.Add("Previous field", PdfIconKind.Left, () => Viewport.NavigateField(true));
        _leftPanel.Add("Field properties", PdfIconKind.Settings, () => Run(EditFieldPropertiesAsync));
        _leftPanel.Add("Delete selected field", PdfIconKind.Trash, () => Safe(() => Session.DeleteField()));
        _leftPanel.Items.Children.Add(PdfTheme.Divider());
        _leftPanel.Add("Reset form", PdfIconKind.Undo, () => Run(async () =>
        {
            var context = _active;
            if (await _dialogs.ConfirmAsync("Reset form values?", "Restores field defaults. This change can be undone.", "Reset")) context.Session.ResetForm();
        }));
        _leftPanel.Add("Import form data", PdfIconKind.File, () => Run(ImportFormDataAsync));
        _leftPanel.Add("Export XFDF form data", PdfIconKind.Export, () => Run(() => ExportFormDataAsync(true)));
        _leftPanel.Add("Export form data", PdfIconKind.Export, () => Run(() => ExportFormDataAsync(false)));
        _leftPanel.Add("Check required fields", PdfIconKind.Check, CheckRequiredFields);
        _leftPanel.Add("Save filled PDF", PdfIconKind.Save, () => Run(() => ExportPdfAsync()));
        _leftPanel.Add("Open form example", PdfIconKind.File, () => Safe(() =>
        {
            AddDocument(PdfDocumentEngine.CreateFormSample(_typeface)); Viewport.Navigate(5); UseTool(PdfTool.FillForm); ShowFormFields();
        }));
        _leftPanel.Description("Text, check boxes, radio groups and single-select choices are supported. XFA, form scripts, calculations and certificate fields are not edited.");
    }
    private void ShowFormFields() { _right = "Form fields"; RefreshRight(); AdaptLayout(); }
    private async Task CreateFieldAsync(DocumentContext context, PdfFieldKind kind, RectD bounds)
    {
        var pageId = context.Session.Page.Id;
        var name = await _dialogs.PromptAsync("Name the form field", "Choose a unique field name. Dragging on the page defines its position and size.", "Field" + (context.Session.Document.FieldCount + 1), acceptLabel: "Create field");
        if (string.IsNullOrWhiteSpace(name)) return;
        var options = Array.Empty<PdfFieldOption>();
        if (kind == PdfFieldKind.ComboBox)
        {
            var choices = await _dialogs.PromptAsync("Dropdown choices", "Enter one choice per line. Values and labels will use the same text.", "Option 1\nOption 2\nOption 3", multiline: true, acceptLabel: "Create choices");
            if (choices is null) return;
            options = choices.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(200).Select(value => new PdfFieldOption(value, value)).ToArray();
            if (options.Length == 0) throw new ArgumentException("Enter at least one dropdown choice.");
        }
        var index = Array.FindIndex(context.Session.Document.Pages, page => page.Id == pageId);
        if (index < 0) throw new InvalidOperationException("The target page was closed or removed.");
        context.Session.Navigate(index);
        var value = kind == PdfFieldKind.CheckBox ? "Off" : options.FirstOrDefault()?.Value ?? "";
        context.Session.AddField(new() { Name = name.Trim(), GroupName = name.Trim(), Label = name.Trim(), Kind = kind, Bounds = bounds, Options = options, Value = value, DefaultValue = value });
        UseTool(PdfTool.FillForm); ShowFormFields();
        ShowStatus("Native form field added. Export PDF keeps it interactive.");
    }
    private Task FillFieldAsync(DocumentContext context, PdfFormFieldState field)
    {
        if (!field.CanFill) { ShowStatus("This field is read-only or uses an unsupported form type.", true); return Task.CompletedTask; }
        context.Session.SelectField(field.Id); ShowFormFields();
        if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
            context.Session.SetFieldValue(field.Id, field.Kind == PdfFieldKind.RadioButton || !field.IsChecked ? field.ExportValue : "Off");
        else if (field.Kind == PdfFieldKind.Text) context.Viewport.BeginFieldText(field);
        return Task.CompletedTask;
    }
    private void BuildFormFields(StackPanel content)
    {
        var context = _active;
        var fields = Session.Document.Pages.SelectMany((page, index) => page.Fields.Select(field => (Index: index, Field: field))).ToArray();
        content.Children.Add(Paragraph($"{fields.Length} interactive fields · {fields.Count(item => MissingRequiredValue(item.Field))} missing required values", 11));
        content.Children.Add(new PdfCommandButton("Fill form on page", PdfIconKind.Select, () => UseTool(PdfTool.FillForm)));
        foreach (var (index, field) in fields.OrderBy(item => item.Field.Id == Session.SelectedFieldId ? 0 : 1).ThenBy(item => item.Index == Session.CurrentPage ? 0 : 1).Take(200))
        {
            var card = PdfTheme.Column(7); card.Margin = new Thickness(10);
            var title = (field.Label.Length > 0 ? field.Label : field.Name) + (field.Required ? " *" : "");
            card.Children.Add(new PdfCommandButton(title, PdfIconKind.Grid, () => { context.Viewport.Navigate(index); context.Session.SelectField(field.Id); UseTool(PdfTool.FillForm); context.Session.SelectField(field.Id); }) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left });
            card.Children.Add(Paragraph($"Page {index + 1} · {field.Kind}" + (field.ReadOnly ? " · Read-only" : ""), 10));
            if (field.Kind == PdfFieldKind.Text)
            {
                var text = new PdfTextField("Field value: " + field.Name) { Text = field.Value, AcceptsReturn = field.Multiline, TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, IsReadOnly = !field.CanFill, MaxLength = field.MaxLength };
                card.Children.Add(text);
                var apply = new PdfCommandButton("Apply " + field.Name, PdfIconKind.Check, () => Safe(() => context.Session.SetFieldValue(field.Id, text.Text))) { IsEnabled = field.CanFill };
                card.Children.Add(apply);
                text.KeyDown += (_, args) => { if (args.Key == VirtualKey.Enter && !field.Multiline) { Safe(() => context.Session.SetFieldValue(field.Id, text.Text)); args.Handled = true; } };
            }
            else if (field.Kind is PdfFieldKind.ComboBox or PdfFieldKind.ListBox)
            {
                foreach (var option in field.Options.Take(50))
                {
                    var choose = new PdfCommandButton(option.Label, PdfIconKind.Check, () => Safe(() => context.Session.SetFieldValue(field.Id, option.Value))) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, IsEnabled = field.CanFill };
                    choose.Select(field.Value == option.Value); card.Children.Add(choose);
                }
            }
            else if (field.Kind is PdfFieldKind.CheckBox or PdfFieldKind.RadioButton)
            {
                var toggle = new PdfCommandButton((field.IsChecked ? "Uncheck " : "Check ") + field.Name, PdfIconKind.Check, () => Safe(() => context.Session.SetFieldValue(field.Id, field.Kind == PdfFieldKind.RadioButton || !field.IsChecked ? field.ExportValue : "Off"))) { IsEnabled = field.CanFill };
                toggle.Select(field.IsChecked); card.Children.Add(toggle);
            }
            else card.Children.Add(Paragraph("This field is preserved but cannot be filled here."));
            if (field.Kind is not (PdfFieldKind.Signature or PdfFieldKind.Unsupported))
                card.Children.Add(new PdfCommandButton("Edit properties: " + field.Name, PdfIconKind.Settings, () =>
                {
                    context.Viewport.Navigate(index); context.Session.SelectField(field.Id); Run(EditFieldPropertiesAsync);
                }) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left });
            content.Children.Add(new Border { Child = card, BorderBrush = PdfTheme.Brush("#DEDEDE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) });
        }
        if (fields.Length == 0) content.Children.Add(Paragraph("No interactive fields yet. Use Prepare a form to draw fields, or open the interactive form example."));
    }
    private Task EditFieldPropertiesAsync()
    {
        if (Session.SelectedField is null) { ShowFormFields(); ShowStatus("Select a field to edit its properties."); return Task.CompletedTask; }
        Viewport.FinishText(true);
        _right = "Field properties"; RefreshRight(); AdaptLayout();
        return Task.CompletedTask;
    }

    private void BuildProtectionTools()
    {
        _leftPanel.Description("Create an encrypted PDF with separate opening and owner/editing passwords. Passwords are masked, kept in memory and never saved in a workspace.");
        void Toggle(string title, bool current, Action<bool> apply)
        {
            var button = _leftPanel.Add(title, PdfIconKind.Check, () => { apply(!current); BuildLeft(); }); button.Select(current);
        }
        Toggle("Allow printing", _permitPrint, value => _permitPrint = value);
        Toggle("Allow copying text", _permitCopy, value => _permitCopy = value);
        Toggle("Allow document editing", _permitEdit, value => _permitEdit = value);
        _leftPanel.Items.Children.Add(PdfTheme.Divider());
        _leftPanel.Add("Encrypt and export PDF", PdfIconKind.Lock, () => Run(ProtectAsync));
        _leftPanel.Description("AES-256 encryption. Permission flags depend on viewer enforcement; they are not DRM. Opening protected PDFs requires owner authorization. Unlocked workspaces are not automatically re-encrypted.");
    }
    private async Task ProtectAsync()
    {
        Viewport.FinishText(true); var context = _active; var document = context.Session.Document;
        var user = await _dialogs.SecretAsync("PDF opening password", "Enter at least eight characters. This password is required to open the exported PDF.");
        if (user is null) return;
        if (user.Length < 8) throw new ArgumentException("Use at least eight characters for the opening password.");
        var confirm = await _dialogs.SecretAsync("Confirm opening password", "Re-enter the opening password to avoid exporting a file you cannot reopen.");
        if (confirm is null) return;
        if (!string.Equals(user, confirm, StringComparison.Ordinal)) throw new ArgumentException("Opening passwords do not match.");
        var owner = await _dialogs.SecretAsync("PDF owner password", "Choose a different password of at least eight characters. Keep it safe: PdfSpace requires it to edit a protected PDF.", "Encrypt PDF");
        if (owner is null) return;
        ShowStatus("Encrypting PDF…"); await Task.Delay(25);
        var result = PdfDocumentEngine.Save(document, _typeface);
        var protectedBytes = await _security.EncryptAsync(result.Bytes, new(user, owner, _permitPrint, _permitCopy, _permitEdit));
        user = confirm = owner = null;
        await _storage.SaveAsync(BaseName(document.Title) + "-protected.pdf", protectedBytes, "application/pdf");
        ShowStatus("AES-256 encrypted PDF download started. Passwords were not saved.");
    }
    private void BuildRedactionTools()
    {
        _leftPanel.Description("Mark sensitive areas, review every mark, then create a separate redacted copy. A red mark or a saved workspace does not remove source content.");
        _leftPanel.Add("Mark for redaction", PdfIconKind.Redact, () => UseTool(PdfTool.Redact), 0xFFB32435);
        _leftPanel.Add("Select redaction marks", PdfIconKind.Select, () => UseTool(PdfTool.Select));
        _leftPanel.Add("Remove all redaction marks", PdfIconKind.Trash, () => Safe(() => Session.Execute("Remove redaction marks", document => document with { Pages = document.Pages.Select(page => page with { Annotations = page.Annotations.Where(annotation => annotation.Kind != AnnotationKind.RedactionMark).ToArray() }).ToArray() })));
        _leftPanel.Items.Children.Add(PdfTheme.Divider());
        _leftPanel.Add("Apply raster redactions", PdfIconKind.Check, () => Run(ApplyRedactionsAsync), 0xFFB32435);
        _leftPanel.Description("This destructive export rasterizes ALL pages and removes original text, forms, links, metadata and attachments. Searchability and vector quality are lost. Original PDFs and editable workspaces remain unredacted.");
    }
    private async Task ApplyRedactionsAsync()
    {
        Viewport.FinishText(true); var context = _active; var document = context.Session.Document;
        var count = document.Pages.Sum(page => page.Annotations.Count(annotation => annotation.Kind == AnnotationKind.RedactionMark));
        if (count == 0) throw new InvalidOperationException("Draw at least one redaction mark first.");
        if (!await _dialogs.ConfirmAsync("Apply raster redactions?", $"Apply {count} marked areas and rebuild all {document.Pages.Length} pages at 144 DPI as images? Text, forms, links, metadata and original PDF objects are discarded. Inspect every page of the exported copy before sharing. Original files and workspaces remain unredacted.", "Apply redactions")) return;
        ShowStatus("Rebuilding pages without original PDF objects…"); await Task.Delay(25);
        var bytes = RasterRedactor.Export(document, context.Viewport.Renderer);
        await _storage.SaveAsync(BaseName(document.Title) + "-redacted.pdf", bytes, "application/pdf");
        AddDocument(PdfDocumentEngine.Open(bytes, BaseName(document.Title) + "-redacted.pdf"));
        SetMode("All tools"); ShowStatus("Redacted image-only copy opened for review. Original tab and workspace still contain the unredacted document.");
    }
    private async Task ShowOriginalTextAsync()
    {
        if (Session.Page.Ocr is not null) { ShowOcrReview(); ShowStatus("Use Correct recognized text to edit this scan’s OCR layer."); return; }
        Viewport.FinishText(true); var context = _active; var document = context.Session.Document; var page = context.Session.CurrentPage;
        ShowStatus("Reading original text operands…"); await Task.Delay(25);
        var runs = PdfTextEditor.Read(document, page);
        if (_active != context || !ReferenceEquals(document, context.Session.Document) || page != context.Session.CurrentPage) return;
        _originalRuns = runs; _right = "Original text"; RefreshRight(); AdaptLayout();
        ShowStatus($"{runs.Count(run => run.Editable)} editable text runs. Replacement uses existing font glyphs; automatic reflow is not supported.");
    }
    private void BuildOriginalText(StackPanel content)
    {
        content.Children.Add(Paragraph("Edits change real text-showing operands in the source PDF. Existing font subsets and positioned glyphs limit replacements; no white-cover simulation is used."));
        content.Children.Add(new PdfCommandButton("Refresh text on current page", PdfIconKind.Rotate, () => Run(ShowOriginalTextAsync)));
        foreach (var run in _originalRuns.Take(200))
        {
            var button = new PdfCommandButton("Edit source text: " + run.Text, PdfIconKind.Text, () => Run(() => ReplaceOriginalTextAsync(run))) { IsEnabled = run.Editable, Height = double.NaN, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Content = Paragraph(run.Text.Length > 200 ? run.Text[..200] + "…" : run.Text, 12, run.Editable ? "#333333" : "#999999");
            ToolTipService.SetToolTip(button, run.Limitation); content.Children.Add(button);
        }
        if (_originalRuns.Length == 0) content.Children.Add(Paragraph("No supported top-level text runs. Scanned pages, paths and text inside nested Form XObjects are not edited by this tool."));
    }
    private async Task ReplaceOriginalTextAsync(PdfTextRun run)
    {
        var context = _active;
        var replacement = await _dialogs.PromptAsync("Edit original PDF text", run.Limitation, run.Text, multiline: true, acceptLabel: "Replace source text");
        if (replacement is null || replacement == run.Text) return;
        context.Session.Execute("Edit original PDF text", document => PdfTextEditor.Replace(document, run, replacement));
        await ShowOriginalTextAsync(); ShowStatus("Original PDF text changed. Undo restores the prior source bytes.");
    }
    private async Task CreateLinkAsync(DocumentContext context, RectD bounds)
    {
        var index = context.Session.CurrentPage;
        var target = await _dialogs.PromptAsync("Link destination", "Enter an HTTPS/HTTP/mailto address, or a one-based page number such as 3.", "https://", acceptLabel: "Create link");
        if (string.IsNullOrWhiteSpace(target)) return;
        int? pageTarget = null; string? uri = null;
        if (int.TryParse(target, out var number)) { if (number < 1 || number > context.Session.Document.Pages.Length) throw new ArgumentException("The destination page does not exist."); pageTarget = number - 1; }
        else
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var address) || address.Scheme is not ("https" or "http" or "mailto")) throw new ArgumentException("Use an HTTP, HTTPS or mailto URL.");
            uri = address.AbsoluteUri;
        }
        context.Session.AddAnnotation(new Annotation { Kind = AnnotationKind.Link, Bounds = bounds, Uri = uri, TargetPage = pageTarget, Text = target }, index);
        ShowStatus("Link added. Use Fill form to follow links or Export PDF to keep them interactive.");
    }
    private async Task FollowLinkAsync(DocumentContext context, Annotation annotation)
    {
        if (annotation.TargetPage is { } page) { context.Viewport.Navigate(page); return; }
        if (annotation.Uri is not { } target || !Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http" or "mailto")) throw new InvalidOperationException("Unsupported link destination.");
        if (await _dialogs.ConfirmAsync("Open external link?", "This PDF links to:\n" + uri.AbsoluteUri + "\n\nOpen it with the browser or default application?", "Open link")) await Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
