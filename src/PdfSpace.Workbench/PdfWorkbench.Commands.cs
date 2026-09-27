using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private async Task OpenAsync(bool combine = false)
    { var file = await _storage.OpenAsync(); if (file is not null) await OpenFileAsync(file, combine); }
    public async Task OpenFileAsync(WorkspaceFile file, bool combine = false)
    {
        var context = _active;
        ShowStatus("Opening " + file.Name + "…"); await Task.Delay(25);
        PdfWorkspace document;
        if (file.Name.EndsWith(".pdfspace", StringComparison.OrdinalIgnoreCase)) document = PdfDocumentEngine.PrepareWorkspace(WorkspaceJson.Load(Encoding.UTF8.GetString(file.Bytes)));
        else
        {
            PdfUnlockResult unlocked;
            try { unlocked = await _security.UnlockAsync(file.Bytes); }
            catch (PdfPasswordRequiredException)
            {
                var password = await _dialogs.SecretAsync("Open protected PDF", "Enter the owner/editing password. Read-only password permissions are not bypassed. The unlocked working copy stays in memory; automatic recovery is disabled.", "Unlock");
                if (password is null) return;
                unlocked = await _security.UnlockAsync(file.Bytes, password);
                password = null;
            }
            document = PdfDocumentEngine.Open(unlocked.Bytes, file.Name);
            if (unlocked.WasEncrypted) document = document with { Sources = document.Sources.Select(source => source with { Sensitive = true }).ToArray() };
        }
        if (combine) { context.Session.Combine(document); RefreshData(); ShowStatus("PDF combined. Use Organize pages to reorder or extract pages."); }
        else
        {
            AddDocument(document);
            ShowStatus(document.IsSensitive ? "Protected PDF unlocked in memory. Automatic recovery disabled; workspace exports are unencrypted." : $"Opened {file.Name}. {document.FieldCount} form fields imported. Original file unchanged.");
            if (document.FieldCount > 0) { UseTool(PdfTool.FillForm); ShowFormFields(); }
        }
    }
    private static string BaseName(string title) => Path.GetFileNameWithoutExtension(title);
    private async Task SaveWorkspaceAsync()
    {
        Viewport.FinishText(true); var context = _active; var document = context.Session.Document;
        if (document.IsSensitive && !await _dialogs.ConfirmAsync("Export unencrypted workspace?", "This workspace contains decrypted original PDF data. It will not be password protected. Use Protect a PDF to export an encrypted PDF instead.", "Export unencrypted")) return;
        await _storage.SaveAsync(BaseName(document.Title) + ".pdfspace", Encoding.UTF8.GetBytes(WorkspaceJson.Save(document)), "application/json");
        if (ReferenceEquals(document, context.Session.Document)) context.Session.MarkSaved(); UpdateTabs(); ShowStatus("Editable workspace download started.");
    }
    private async Task ExportPdfAsync(int[]? pages = null)
    {
        Viewport.FinishText(true); var context = _active; var document = context.Session.Document;
        if (document.IsSensitive && !await _dialogs.ConfirmAsync("Export unencrypted PDF?", "The unlocked source is not automatically re-encrypted. Use Protect a PDF to set new passwords, or explicitly continue with an unencrypted copy.", "Export unencrypted")) return;
        ShowStatus("Preparing structured PDF…"); await Task.Delay(25);
        if (pages is not null) document = WorkspacePages.Select(document, pages);
        var result = PdfDocumentEngine.Save(document, _typeface);
        await _storage.SaveAsync(BaseName(document.Title) + (pages is null ? "-reviewed.pdf" : "-extracted.pdf"), result.Bytes, "application/pdf");
        ShowStatus(result.PreservedSourceCatalog ? "PDF saved with native annotations/forms and original page content. Source catalog retained; no signature preservation claim." : "PDF pages assembled with native content. Original document-level structures may change.");
    }
    private async Task ExportFlattenedAsync()
    {
        Viewport.FinishText(true); var context = _active;
        if (!await _dialogs.ConfirmAsync("Export flattened visual PDF?", "Creates a separate visual copy. Interactive forms, links, annotations, metadata structures and digital signatures are not retained. This does not apply redactions or preserve password protection.", "Export flattened")) return;
        if (context.Session.Document.Pages.Any(page => page.Annotations.Any(annotation => annotation.Kind == AnnotationKind.RedactionMark))) throw new InvalidOperationException("Apply pending redactions before exporting a normal visual copy.");
        await _storage.SaveAsync(BaseName(context.Session.Document.Title) + "-flattened.pdf", context.Viewport.Renderer.ExportPdf(context.Session.Document), "application/pdf");
        ShowStatus("Flattened visual PDF download started.");
    }
    private async Task ExportPngAsync()
    {
        Viewport.FinishText(true); ShowStatus("Rendering page image…"); await Task.Delay(25);
        var bytes = Viewport.Renderer.ExportPng(Session.Document, Session.CurrentPage, 2);
        await _storage.SaveAsync($"{BaseName(Session.Document.Title)}-page-{Session.CurrentPage + 1}.png", bytes, "image/png"); ShowStatus("PNG image download started.");
    }
    private async Task ExportTextAsync()
    {
        ShowStatus("Extracting selectable text…"); await Task.Delay(25);
        await _storage.SaveAsync(BaseName(Session.Document.Title) + ".txt", Encoding.UTF8.GetBytes(PdfReader.ExtractText(Session.Document)), "text/plain;charset=utf-8"); ShowStatus("Text download started. Unrecognized scans need Scan & OCR first.");
    }
    private async Task PrintAsync()
    {
        Viewport.FinishText(true); var bytes = Viewport.Renderer.ExportPdf(Session.Document);
        await _storage.PrintAsync(BaseName(Session.Document.Title) + "-print.pdf", bytes); ShowStatus("Printable PDF opened. Use the PDF viewer's print command.");
    }
    private async Task ExtractPagesAsync()
    {
        var value = await _dialogs.PromptAsync("Extract pages", "Enter page numbers and ranges, for example 1, 3-5. Extraction keeps supported native page content. Links to excluded pages are removed.", (Session.CurrentPage + 1).ToString(CultureInfo.InvariantCulture), acceptLabel: "Extract");
        if (value is not null) await ExportPdfAsync(PageRange.Parse(value, Session.Document.Pages.Length));
    }
    private async Task SplitAsync()
    {
        if (!await _dialogs.ConfirmAsync("Split document", "Create a ZIP containing one visual PDF per page? Original PDF files remain unchanged.", "Split")) return;
        if (Session.Document.Pages.Length > 300) throw new InvalidOperationException("Split up to 300 pages at a time in this version. Extract a range first for larger documents.");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            for (var i = 0; i < Session.Document.Pages.Length; i++)
            {
                ShowStatus($"Splitting page {i + 1} of {Session.Document.Pages.Length}…"); await Task.Delay(1);
                var bytes = Viewport.Renderer.ExportPdf(Session.Document, [i]); var entry = zip.CreateEntry($"page-{i + 1:000}.pdf", CompressionLevel.Fastest); using var output = entry.Open(); output.Write(bytes);
                if (stream.Length > 256 * 1024 * 1024) throw new InvalidOperationException("Split archive exceeded 256 MB. Extract a smaller range first.");
            }
        }
        await _storage.SaveAsync(BaseName(Session.Document.Title) + "-pages.zip", stream.ToArray(), "application/zip"); ShowStatus("Split PDF archive download started.");
    }
    private async Task SearchAsync(string query)
    {
        _searchQuery = query; _right = "Find"; ShowStatus("Searching document…"); await Task.Delay(25);
        _searchResults = PdfReader.Find(Session.Document, query, _matchCase).Take(500).ToArray(); RefreshRight(); AdaptLayout();
        if (_searchResults.Length > 0) Viewport.HighlightSearch(_searchResults[0]); ShowStatus($"{_searchResults.Length} results for “{query}”.");
    }
    private async Task DeletePageAsync()
    { if (await _dialogs.ConfirmAsync("Delete this page?", "The page will be removed from this workspace. You can undo the change.", "Delete page")) Session.DeletePage(); }
    private async Task SetZoomAsync()
    {
        var input = await _dialogs.PromptAsync("Zoom", "Enter a percentage between 10 and 800.", (Viewport.Zoom * 100).ToString("F0", CultureInfo.InvariantCulture));
        if (input is not null && double.TryParse(input.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)) Viewport.ZoomTo(value / 100);
    }
    private async Task BookmarkAsync()
    {
        var name = await _dialogs.PromptAsync("Bookmark page", "Bookmarks are retained in editable workspaces and supported native PDF exports.", Session.Page.Bookmark.Length > 0 ? Session.Page.Bookmark : "Page " + (Session.CurrentPage + 1));
        if (name is not null) Session.BookmarkPage(name.Trim());
    }
    private async Task EditSelectedAsync()
    {
        if (Session.SelectedAnnotation is not { } annotation) return;
        if (annotation.Kind == AnnotationKind.Text) { Viewport.BeginText(annotation); return; }
        var text = await _dialogs.PromptAsync("Edit annotation", "Edit the comment or stamp text.", annotation.Text, true);
        if (text is not null) Session.UpdateAnnotation(annotation.Id, a => a with { Text = text });
    }
    private async Task ChooseStampAsync()
    {
        var text = await _dialogs.PromptAsync("Add stamp", "Choose the stamp text, then click on a page to place it.", "APPROVED", acceptLabel: "Use stamp");
        if (!string.IsNullOrWhiteSpace(text)) { Session.PendingText = text.Trim(); UseTool(PdfTool.Stamp); }
    }
    private async Task AddInitialsAsync()
    {
        var text = await _dialogs.PromptAsync("Add initials", "Enter your initials. The mark is visual, not a digital signature.", "");
        if (string.IsNullOrWhiteSpace(text)) return;
        Session.AddAnnotation(new Annotation { Kind = AnnotationKind.Text, Text = text, FontSize = 20, Bounds = new(Session.Page.VisibleBox.Center.X - 50, Session.Page.VisibleBox.Center.Y, 120, 35), Color = Session.Color }); UseTool(PdfTool.Select);
    }
    private async Task AddWatermarkAsync()
    {
        var text = await _dialogs.PromptAsync("Add watermark", "Add a translucent text annotation to every page. This is a visual label, not a security feature.", "DRAFT");
        if (string.IsNullOrWhiteSpace(text)) return;
        Session.Execute("Add watermark", d => d with { Pages = d.Pages.Select(p => p with { Annotations = [..p.Annotations, new Annotation { Kind = AnnotationKind.Text, Text = text, FontSize = 48, Color = 0x40777777, Bounds = new(p.VisibleBox.X + 50, p.VisibleBox.Center.Y - 35, p.VisibleBox.Width - 100, 100) }] }).ToArray() });
    }
    private async Task AddPageNumbersAsync()
    {
        var template = await _dialogs.PromptAsync("Add page numbers", "Use {page} and {pages} for the current page and total count.", "{page} / {pages}");
        if (template is null) return;
        Session.Execute("Add page numbers", d => d with { Pages = d.Pages.Select((p, i) => p with { Annotations = [..p.Annotations, new Annotation { Kind = AnnotationKind.Text, Text = template.Replace("{page}", (i + 1).ToString()).Replace("{pages}", d.Pages.Length.ToString()), FontSize = 10, Color = 0xFF777777, Bounds = new(p.VisibleBox.X + 25, p.VisibleBox.Bottom - 25, p.VisibleBox.Width - 50, 18) }] }).ToArray() });
    }
    private async Task EditInfoAsync()
    {
        var title = await _dialogs.PromptAsync("Document title", "This changes the workspace title and exported PDF metadata, not the source file.", Session.Document.Title);
        if (!string.IsNullOrWhiteSpace(title)) Session.Execute("Rename document", d => d with { Title = Path.GetFileName(title.Trim()) });
    }
    private async Task ShowHelpAsync()
    {
        var version = typeof(PdfWorkbench).Assembly.GetName().Version?.ToString(3) ?? "development";
        await _dialogs.PromptAsync($"PdfSpace · {version} alpha", "Ctrl/Cmd+O — open PDF or workspace\nCtrl/Cmd+S — save editable workspace\nCtrl/Cmd+Shift+S — export PDF\nCtrl/Cmd+F — find text\nCtrl/Cmd+Z / Shift+Z — undo / redo\nCtrl/Cmd+C — copy selected text\nPage Up / Page Down — navigate pages\nV — select · H — hand · T — add text · D — draw\nEscape — cancel · Delete — delete annotation\nCtrl+wheel — zoom around pointer\n\nPdfSpace is an independent Uno Platform / SkiaSharp app, not Adobe Acrobat. Structured PDF export supports native annotations and AcroForms. Original text editing uses existing font glyphs without paragraph reflow. Redaction rebuilds all pages as raster images. Password export uses AES-256. XFA, OCR, certificate signing/trust, full accessibility/compliance and cloud services remain unsupported.", acceptLabel: "Close", input: false);
    }
    private void ShowFileMenu()
    {
        var flyout = new Flyout { FlyoutPresenterStyle = (Style)PdfResources.Shared["FlyoutStyle"] }; var content = PdfTheme.Column(2);
        void Add(string label, PdfIconKind icon, Func<Task> action)
        { content.Children.Add(new PdfCommandButton(label, icon, () => { flyout.Hide(); Run(action); }) { MinWidth = 264, HorizontalContentAlignment = HorizontalAlignment.Left }); }
        Add("Open…                  Ctrl+O", PdfIconKind.Folder, () => OpenAsync());
        Add("New PDF                Ctrl+N", PdfIconKind.File, () => { AddDocument(new PdfWorkspace()); return Task.CompletedTask; });
        Add("Save workspace       Ctrl+S", PdfIconKind.Save, SaveWorkspaceAsync);
        Add("Export PDF…", PdfIconKind.Export, () => ExportPdfAsync());
        Add("Print…                     Ctrl+P", PdfIconKind.Print, PrintAsync);
        content.Children.Add(PdfTheme.Divider());
        Add("Continuous pages", PdfIconKind.Pages, () => { Viewport.SetLayout(PageLayoutMode.Continuous); return Task.CompletedTask; });
        Add("Single page", PdfIconKind.File, () => { Viewport.SetLayout(PageLayoutMode.SinglePage); return Task.CompletedTask; });
        Add("Two-page view", PdfIconKind.Grid, () => { Viewport.SetLayout(PageLayoutMode.TwoPage); return Task.CompletedTask; });
        Add("Fit to width", PdfIconKind.FitWidth, () => { Viewport.FitPage(true); return Task.CompletedTask; });
        content.Children.Add(PdfTheme.Divider());
        Add("Clear local recovery…", PdfIconKind.Trash, async () => { if (await _dialogs.ConfirmAsync("Clear recovery copy?", "This removes the automatic recovery file from this device, not downloaded workspaces or PDFs.", "Clear recovery")) { _autosave.Stop(); await _storage.ClearRecoveryAsync(); ShowStatus("Local recovery copy cleared."); } });
        Add("About and keyboard shortcuts", PdfIconKind.Help, ShowHelpAsync);
        flyout.Content = content; flyout.ShowAt(_menuButton);
    }
    private void ShowContextMenu(Point point)
    {
        var flyout = new Flyout { FlyoutPresenterStyle = (Style)PdfResources.Shared["FlyoutStyle"] }; var content = PdfTheme.Column(2);
        void Add(string name, PdfIconKind icon, Action action) { content.Children.Add(new PdfCommandButton(name, icon, () => { flyout.Hide(); Safe(action); }) { MinWidth = 190, HorizontalContentAlignment = HorizontalAlignment.Left }); }
        Add("Select", PdfIconKind.Select, () => UseTool(PdfTool.Select)); Add("Hand", PdfIconKind.Hand, () => UseTool(PdfTool.Hand)); Add("Add text", PdfIconKind.Text, () => UseTool(PdfTool.Text)); Add("Add comment", PdfIconKind.Comment, () => UseTool(PdfTool.Note));
        Add("Copy selected text", PdfIconKind.Copy, () => Run(CopyTextAsync)); Add("Delete annotation", PdfIconKind.Trash, () => Session.DeleteSelection()); Add("Rotate page", PdfIconKind.Rotate, () => Session.RotatePage());
        flyout.Content = content; flyout.ShowAt(Viewport, new FlyoutShowOptions { Position = point });
    }
    private async Task CopyTextAsync()
    {
        var text = Viewport.SelectedText.Length > 0 ? Viewport.SelectedText : Session.SelectedAnnotation?.Text;
        if (string.IsNullOrEmpty(text)) { ShowStatus("Drag across selectable text, then copy."); return; }
        await _storage.CopyTextAsync(text); ShowStatus("Text copied to clipboard.");
    }
    private static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private void Keyboard(object sender, KeyRoutedEventArgs e)
    {
        if (_dialogs.IsOpen) return;
        var command = Down(VirtualKey.Control) || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows); var shift = Down(VirtualKey.Shift);
        if (command)
        {
            switch (e.Key)
            {
                case VirtualKey.O: Run(() => OpenAsync()); break;
                case VirtualKey.N: AddDocument(new PdfWorkspace()); break;
                case VirtualKey.S: Run(shift ? () => ExportPdfAsync() : SaveWorkspaceAsync); break;
                case VirtualKey.F: _right = "Find"; RefreshRight(); AdaptLayout(); break;
                case VirtualKey.P: Run(PrintAsync); break;
                case VirtualKey.Z: if (Viewport.IsEditingText) return; if (shift) Session.Redo(); else Session.Undo(); break;
                case VirtualKey.Y: if (Viewport.IsEditingText) return; Session.Redo(); break;
                case VirtualKey.C: if (FocusManager.GetFocusedElement(XamlRoot) is TextBox) return; Run(CopyTextAsync); break;
                case VirtualKey.Number0: Viewport.FitPage(); break;
                case VirtualKey.Number1: Viewport.ZoomTo(1); break;
                default: return;
            }
            e.Handled = true; return;
        }
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox || Viewport.IsEditingText) return;
        switch (e.Key)
        {
            case VirtualKey.V: UseTool(PdfTool.Select); break;
            case VirtualKey.H: UseTool(PdfTool.Hand); break;
            case VirtualKey.T: UseTool(PdfTool.Text); break;
            case VirtualKey.D: UseTool(PdfTool.Ink); break;
            default: return;
        }
        e.Handled = true;
    }
}
