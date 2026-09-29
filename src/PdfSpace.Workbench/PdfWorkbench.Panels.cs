using System.Globalization;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private SearchResult[] _searchResults = [];
    private string _searchQuery = "";
    private bool _matchCase, _wholeWord, _hideResolved;
    private void BuildLeft()
    {
        if (_active is null) return;
        _leftPanel.Items.Children.Clear(); _leftPanel.Title = _mode;
        if (_toolButtons.Count > 7) _toolButtons.RemoveRange(7, _toolButtons.Count - 7);
        void Tool(string name, PdfIconKind icon, PdfTool tool)
        { var button = _leftPanel.Add(name, icon, () => UseTool(tool)); _toolButtons.Add((tool, button)); }
        switch (_mode)
        {
            case "All tools":
                _leftPanel.Add("Prepare a form", PdfIconKind.Grid, () => SetMode("Prepare a form"), 0xFF7254C6);
                _leftPanel.Add("Edit objects", PdfIconKind.Select, () => Safe(ShowObjects));
                _leftPanel.Add("Edit original text", PdfIconKind.Text, () => Run(ShowOriginalTextAsync), 0xFFD93830);
                _leftPanel.Add("Edit a PDF", PdfIconKind.Edit, () => SetMode("Edit"), 0xFFD93830);
                _leftPanel.Add("Export a PDF", PdfIconKind.Export, () => SetMode("Convert"), 0xFF278748);
                _leftPanel.Add("Organize pages", PdfIconKind.Pages, () => SetMode("Organize pages"), 0xFF9063C9);
                _leftPanel.Add("Add comments", PdfIconKind.Comment, () => { OpenRight("Comments"); UseTool(PdfTool.Note); }, 0xFFE19815);
                _leftPanel.Add("Fill & Sign", PdfIconKind.Sign, () => SetMode("E-Sign"), 0xFF9257C5);
                _leftPanel.Add("Create a PDF", PdfIconKind.File, () => AddDocument(new PdfWorkspace()), 0xFFD93830);
                _leftPanel.Add("Combine files", PdfIconKind.Copy, () => Run(() => OpenAsync(true)), 0xFF7854BD);
                _leftPanel.Add("Crop pages", PdfIconKind.Crop, () => { SetMode("Edit"); UseTool(PdfTool.Crop); }, 0xFF6D59B1);
                _leftPanel.Add("Measure distance", PdfIconKind.Measure, () => UseTool(PdfTool.Measure), 0xFF577F9C);
                _leftPanel.Items.Children.Add(PdfTheme.Divider());
                _leftPanel.Add("Save editable workspace", PdfIconKind.Save, () => Run(SaveWorkspaceAsync));
                _leftPanel.Add("Find in document", PdfIconKind.Search, () => OpenRight("Find"));
                _leftPanel.Heading("ADVANCED TOOLS");
                _leftPanel.Add("Protect a PDF", PdfIconKind.Lock, () => SetMode("Protect a PDF"), 0xFF52768E);
                _leftPanel.Add("Redact a PDF", PdfIconKind.Redact, () => SetMode("Redact a PDF"), 0xFFBA2B35);
                _leftPanel.Add("Scan & OCR", PdfIconKind.Image, () => SetMode("Scan & OCR"), 0xFF1473E6);
                _leftPanel.Description("Local-first PDF tools. No account, upload or subscription required.");
                break;
            case "Scan & OCR": BuildOcrTools(); break;
            case "Edit":
                _leftPanel.Description("Edit supported original text operands or add text, shapes and annotations. Changes remain undoable in the workspace.");
                _leftPanel.Add("Edit objects", PdfIconKind.Select, () => Safe(ShowObjects));
                _leftPanel.Add("Edit original text", PdfIconKind.Edit, () => Run(ShowOriginalTextAsync));
                _leftPanel.Add("Edit original images", PdfIconKind.Image, () => Safe(ShowNativeImages));
                _leftPanel.Add("Add image", PdfIconKind.Image, () => Run(ChooseInsertImageAsync));
                Tool("Add link", PdfIconKind.Share, PdfTool.Link);
                Tool("Select annotation", PdfIconKind.Select, PdfTool.Select); Tool("Add text", PdfIconKind.Text, PdfTool.Text);
                _leftPanel.Heading("MARK UP TEXT"); Tool("Highlight text", PdfIconKind.Highlight, PdfTool.Highlight); Tool("Underline text", PdfIconKind.Underline, PdfTool.Underline); Tool("Strikethrough text", PdfIconKind.Strikeout, PdfTool.Strikeout);
                _leftPanel.Heading("DRAWING TOOLS"); Tool("Draw freehand", PdfIconKind.Pen, PdfTool.Ink); Tool("Rectangle", PdfIconKind.Rectangle, PdfTool.Rectangle); Tool("Ellipse", PdfIconKind.Ellipse, PdfTool.Ellipse); Tool("Line", PdfIconKind.Line, PdfTool.Line); Tool("Arrow", PdfIconKind.Arrow, PdfTool.Arrow);
                _leftPanel.Add("Add stamp", PdfIconKind.Check, () => Run(ChooseStampAsync));
                _leftPanel.Heading("APPEARANCE"); var palette = new PdfColorPalette(Session.Color); palette.ColorChanged += color => Session.Color = color; _leftPanel.Items.Children.Add(palette);
                AddToolNumber("Font size", Session.FontSize, value => Session.FontSize = Math.Clamp(value, 4, 200));
                AddToolNumber("Stroke width", Session.StrokeWidth, value => Session.StrokeWidth = Math.Clamp(value, .5, 30));
                _leftPanel.Heading("PAGE CONTENT"); Tool("Crop page", PdfIconKind.Crop, PdfTool.Crop);
                _leftPanel.Add("Reset page crop", PdfIconKind.FitPage, () => Safe(() => Session.CropPage(null)));
                _leftPanel.Add("Add watermark", PdfIconKind.Text, () => Run(AddWatermarkAsync));
                _leftPanel.Add("Add page numbers", PdfIconKind.Pages, () => Run(AddPageNumbersAsync));
                _leftPanel.Add("Header and footer", PdfIconKind.Text, () => Safe(() => ShowPageMarks(PdfPageMarkKind.HeaderFooter)));
                _leftPanel.Add("Bates numbering", PdfIconKind.Pages, () => Safe(() => ShowPageMarks(PdfPageMarkKind.HeaderFooter, true)));
                break;
            case "Convert":
                _leftPanel.Description("Structured PDF retains native page content, annotations and supported forms. Flattened visual output remains a separate option.");
                _leftPanel.Add("Flattened visual PDF", PdfIconKind.Image, () => Run(ExportFlattenedAsync));
                _leftPanel.Add("PDF document", PdfIconKind.File, () => Run(() => ExportPdfAsync()), 0xFFD93830);
                _leftPanel.Add("PNG image · current page", PdfIconKind.Image, () => Run(ExportPngAsync), 0xFF29834B);
                _leftPanel.Add("Plain text", PdfIconKind.Text, () => Run(ExportTextAsync), 0xFF1473E6);
                _leftPanel.Add("Editable workspace", PdfIconKind.Save, () => Run(SaveWorkspaceAsync), 0xFF9254CC);
                _leftPanel.Add("Split into single-page PDFs", PdfIconKind.Pages, () => Run(SplitAsync), 0xFF9254CC);
                _leftPanel.Items.Children.Add(PdfTheme.Divider());
                _leftPanel.Add("Extract selected pages", PdfIconKind.Export, () => Run(ExtractPagesAsync));
                _leftPanel.Description("Original PDF files are never overwritten. Keep a .pdfspace file to retain editable notes, drawing marks and page organization.");
                break;
            case "Prepare a form":
                BuildFormTools(); break;
            case "Protect a PDF":
                BuildProtectionTools(); break;
            case "Redact a PDF":
                BuildRedactionTools(); break;
            case "E-Sign":
                _leftPanel.Add("Fill interactive fields", PdfIconKind.Grid, () => { UseTool(PdfTool.FillForm); ShowFormFields(); });
                _leftPanel.Description("Fill a document with text and check marks, then draw your signature.");
                Tool("Add text", PdfIconKind.Text, PdfTool.Text); Tool("Add check mark", PdfIconKind.Check, PdfTool.Check); Tool("Draw signature", PdfIconKind.Sign, PdfTool.Signature);
                _leftPanel.Add("Add initials", PdfIconKind.Text, () => Run(AddInitialsAsync));
                _leftPanel.Add("Add approval stamp", PdfIconKind.Check, () => { Session.PendingText = "APPROVED"; UseTool(PdfTool.Stamp); });
                _leftPanel.Items.Children.Add(PdfTheme.Divider()); _leftPanel.Add("Export signed copy", PdfIconKind.Export, () => Run(() => ExportPdfAsync()));
                _leftPanel.Description("Signatures here are visual marks. This app does not create, validate or preserve certificate-based digital signatures, legal identity verification or audit trails.");
                break;
            case "Organize pages":
                _leftPanel.Description("Select a page. Drag a thumbnail onto another position to reorder pages.");
                _leftPanel.Add("Rotate clockwise", PdfIconKind.Rotate, () => Safe(() => Session.RotatePage()));
                _leftPanel.Add("Rotate counterclockwise", PdfIconKind.Rotate, () => Safe(() => Session.RotatePage(-90)));
                _leftPanel.Add("Move page earlier", PdfIconKind.Up, () => Safe(() => Session.MovePage(Session.CurrentPage - 1)));
                _leftPanel.Add("Move page later", PdfIconKind.Down, () => Safe(() => Session.MovePage(Session.CurrentPage + 1)));
                _leftPanel.Add("Duplicate page", PdfIconKind.Copy, () => Safe(() => Session.DuplicatePage()));
                _leftPanel.Add("Delete page", PdfIconKind.Trash, () => Run(DeletePageAsync));
                _leftPanel.Items.Children.Add(PdfTheme.Divider());
                _leftPanel.Add("Insert blank page", PdfIconKind.Plus, () => Safe(() => Session.InsertBlank()));
                _leftPanel.Add("Insert from PDF", PdfIconKind.Folder, () => Run(() => OpenAsync(true)));
                _leftPanel.Add("Extract pages", PdfIconKind.Export, () => Run(ExtractPagesAsync));
                _leftPanel.Add("Split into PDFs", PdfIconKind.Pages, () => Run(SplitAsync));
                _leftPanel.Items.Children.Add(PdfTheme.Divider());
                _leftPanel.Add("Back to document", PdfIconKind.Left, () => SetMode("All tools"));
                break;
        }
        UpdateChrome();
    }
    private void AddToolNumber(string title, double value, Action<double> apply)
    {
        var row = new Grid { Margin = new Thickness(8, 5, 8, 5), ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(75) } } };
        PdfTheme.Place(row, PdfTheme.Text(title, 12, "#656565")); var field = new PdfTextField(title) { Text = value.ToString(CultureInfo.InvariantCulture) };
        field.LostFocus += (_, _) => { if (double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) apply(number); };
        PdfTheme.Place(row, field, column: 1); _leftPanel.Items.Children.Add(row);
    }
    private void OpenRight(string name)
    { _right = _right == name ? "" : name; RefreshRight(); AdaptLayout(); }
    private void RefreshRight()
    {
        DetachObjectList();
        _rightHost.Content = null; if (_right.Length == 0 || _active is null) return;
        var root = new Grid { Background = PdfTheme.Brush("#FFFFFF"), BorderBrush = PdfTheme.Brush("#D8D8D8"), BorderThickness = new Thickness(1, 0, 0, 0), RowDefinitions = { new() { Height = new GridLength(55) }, new() { Height = new GridLength(1, GridUnitType.Star) } } };
        var header = new Grid { Margin = new Thickness(18, 0, 10, 0), ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        PdfTheme.Place(header, PdfTheme.Text(_right, 16, bold: true)); PdfTheme.Place(header, new PdfCommandButton("Close " + _right, PdfIconKind.Close, () => { _right = ""; RefreshRight(); AdaptLayout(); }, true), column: 1); PdfTheme.Place(root, header);
        if (_right == "Page thumbnails")
        {
            var thumbnails = new PdfThumbnailView(Viewport); PdfTheme.Place(root, thumbnails, row: 1); thumbnails.RevealCurrent();
        }
        else
        {
            var content = PdfTheme.Column(10); content.Margin = new Thickness(15, 5, 15, 24);
            switch (_right)
            {
                case "Page marks": BuildPageMarks(content); break;
                case "Recognized text": BuildOcrReview(content); break;
                case "Objects": BuildObjects(content); break;
                case "Comments": BuildComments(content); break;
                case "Bookmarks": BuildBookmarks(content); break;
                case "Find": BuildFind(content); break;
                case "Properties": BuildProperties(content); break;
                case "Form fields": BuildFormFields(content); break;
                case "Field properties": BuildFieldProperties(content); break;
                case "Original text": BuildOriginalText(content); break;
                case "Original images": BuildNativeImages(content); break;
            }
            PdfTheme.Place(root, new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, row: 1);
        }
        _rightHost.Content = root;
    }
    private static TextBlock Paragraph(string text, double size = 12, string color = "#737373")
    { var label = PdfTheme.Text(text, size, color); label.TextWrapping = TextWrapping.Wrap; label.TextTrimming = TextTrimming.None; return label; }
    private void BuildComments(StackPanel content)
    {
        var filter = new PdfCommandButton(_hideResolved ? "Show all comments" : "Hide resolved", PdfIconKind.Check, () => { _hideResolved = !_hideResolved; RefreshRight(); }) { HorizontalAlignment = HorizontalAlignment.Stretch }; content.Children.Add(filter);
        var comments = Session.Document.Pages.SelectMany((p, i) => p.Annotations.Select(a => (Page: i, Annotation: a))).Where(x => !_hideResolved || !x.Annotation.Resolved).ToArray();
        content.Children.Add(Paragraph(comments.Length + " comments and annotations", 11));
        foreach (var (index, annotation) in comments.Take(200))
        {
            var card = PdfTheme.Column(8); card.Margin = new Thickness(11);
            var heading = PdfTheme.Row(7); heading.Children.Add(new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = PdfTheme.Brush("#E9E5F5"), Child = new TextBlock { Text = "Y", FontFamily = PdfTheme.Font, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }); heading.Children.Add(PdfTheme.Text(annotation.Author, 12, bold: true)); card.Children.Add(heading);
            var open = new PdfCommandButton($"Page {index + 1} · {annotation.Kind}", action: () => { Viewport.Navigate(index); Session.Select(annotation.Id); }) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 27, Padding = new Thickness(0) }; card.Children.Add(open);
            if (annotation.Text.Length > 0) card.Children.Add(Paragraph(annotation.Text, 12, "#404040"));
            card.Children.Add(Paragraph(annotation.Resolved ? "Resolved" : annotation.Created.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture), 10));
            foreach (var reply in annotation.Replies) card.Children.Add(new Border { BorderBrush = PdfTheme.Brush("#DDDDDD"), BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(8, 2, 0, 2), Child = Paragraph(reply.Author + ": " + reply.Text, 11, "#555555") });
            var actions = PdfTheme.Row(4);
            actions.Children.Add(new PdfCommandButton("Reply", action: () => Run(async () => { var reply = await _dialogs.PromptAsync("Reply to comment", "Your reply stays with this workspace.", multiline: true, acceptLabel: "Reply"); if (reply is not null) { Viewport.Navigate(index); Session.Reply(annotation.Id, reply); } })) { Height = 27, Padding = new Thickness(5, 2, 5, 2) });
            actions.Children.Add(new PdfCommandButton(annotation.Resolved ? "Reopen" : "Resolve", action: () => { Viewport.Navigate(index); Session.UpdateAnnotation(annotation.Id, a => a with { Resolved = !a.Resolved }, "Resolve comment"); }) { Height = 27, Padding = new Thickness(5, 2, 5, 2) }); card.Children.Add(actions);
            content.Children.Add(new Border { Child = card, BorderBrush = PdfTheme.Brush("#DEDEDE"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Background = PdfTheme.Brush(annotation.Resolved ? "#F7F7F7" : "#FFFFFF") });
        }
        if (comments.Length == 0)
        {
            content.Children.Add(Paragraph("Start a conversation", 16, "#343434")); content.Children.Add(Paragraph("Use the comment tool to pin a note to a page. Highlighted text and drawing annotations will also appear here.")); content.Children.Add(new PdfCommandButton("Add a comment", PdfIconKind.Comment, () => UseTool(PdfTool.Note)));
        }
        if (comments.Length > 200) content.Children.Add(Paragraph("Showing the first 200 items. Resolve or filter comments to narrow the review."));
    }
    private readonly WorkspaceSnapshotStamp _bookmarkDocument = new();
    private IReadOnlyList<PdfBookmark> _sourceBookmarks = [];
    private void BuildBookmarks(StackPanel content)
    {
        content.Children.Add(new PdfCommandButton("Bookmark current page", PdfIconKind.Plus, () => Run(BookmarkAsync)));
        var bookmarks = Session.Document.Pages.Select((page, index) => (Page: page, Index: index)).Where(item => item.Page.Bookmark.Length > 0).ToArray();
        foreach (var item in bookmarks)
            content.Children.Add(new PdfCommandButton(item.Page.Bookmark, PdfIconKind.Bookmark, () => Viewport.Navigate(item.Index)) { HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch });
        if (bookmarks.Length == 0) content.Children.Add(Paragraph("Add bookmarks for important pages. New bookmarks are retained in native PDF exports and editable workspaces."));
        content.Children.Add(new PdfCommandButton("Remove current bookmark", PdfIconKind.Trash, () => Safe(() => Session.BookmarkPage(""))));
        content.Children.Add(PdfTheme.Divider()); content.Children.Add(PdfTheme.Text("DOCUMENT BOOKMARKS", 10, "#777777", true));
        try
        {
            if (!_bookmarkDocument.Matches(Session.Document))
            { _sourceBookmarks = PdfNavigation.ReadBookmarks(Session.Document); _bookmarkDocument.Remember(Session.Document); }
            foreach (var entry in _sourceBookmarks.Take(1000))
            {
                var target = entry.PageIndex;
                var button = new PdfCommandButton("Document bookmark: " + entry.Title, PdfIconKind.Bookmark, () => { if (target is { } index) Viewport.Navigate(index); })
                {
                    IsEnabled = target is not null, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(Math.Min(entry.Depth, 6) * 12, 0, 0, 0)
                };
                button.Content = Paragraph(entry.Title, 12, target is null ? "#888888" : "#333333");
                ToolTipService.SetToolTip(button, entry.SourceName + (target is { } page ? $" · Page {page + 1}" : " · No supported local destination"));
                content.Children.Add(button);
            }
            if (_sourceBookmarks.Count == 0) content.Children.Add(Paragraph("This source has no additional document bookmarks."));
            if (_sourceBookmarks.Count > 1000) content.Children.Add(Paragraph("Showing the first 1,000 source bookmarks."));
        }
        catch (Exception ex) { content.Children.Add(Paragraph("Source bookmarks could not be read: " + ex.Message)); }
    }
    private void BuildFind(StackPanel content)
    {
        var search = new PdfTextField("Search query", "Find in this document") { Text = _searchQuery };
        search.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Run(() => SearchAsync(search.Text)); e.Handled = true; } }; content.Children.Add(search);
        var actions = PdfTheme.Row(4); actions.Children.Add(new PdfCommandButton("Search", PdfIconKind.Search, () => Run(() => SearchAsync(search.Text))));
        var match = new PdfCommandButton("Match case", action: () => { _matchCase = !_matchCase; Run(() => SearchAsync(search.Text)); }); match.Select(_matchCase); actions.Children.Add(match); content.Children.Add(actions);
        var whole = new PdfCommandButton("Whole words", action: () => { _wholeWord = !_wholeWord; Run(() => SearchAsync(search.Text)); }); whole.Select(_wholeWord); content.Children.Add(whole);
        content.Children.Add(Paragraph(_searchQuery.Length == 0 ? "Search selectable PDF text and annotation text." : $"{_searchResults.Length} results", 11));
        foreach (var result in _searchResults.Take(500))
        {
            var button = new PdfCommandButton("Find result on page " + (result.PageIndex + 1), action: () => Viewport.HighlightSearch(result)) { Height = double.NaN, MinHeight = 62, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            var card = PdfTheme.Column(4); card.Children.Add(PdfTheme.Text("Page " + (result.PageIndex + 1), 11, "#1473E6", true)); card.Children.Add(Paragraph(result.Text, 12, "#474747")); button.Content = card; content.Children.Add(button);
        }
    }
    private void BuildProperties(StackPanel content)
    {
        content.Children.Add(PdfTheme.Text("DOCUMENT", 10, "#777777", true));
        content.Children.Add(Paragraph(Session.Document.Title, 14, "#333333"));
        content.Children.Add(Paragraph($"{Session.Document.Pages.Length} pages\n{Session.Document.Sources.Sum(s => s.Bytes.Length) / 1024.0:F0} KB of original source data\n{Session.Page.Width:F1} × {Session.Page.Height:F1} pt\nRotation: {Session.Page.Rotation}°", 12));
        content.Children.Add(new PdfCommandButton("Edit document information", PdfIconKind.Edit, () => Run(EditInfoAsync)));
        content.Children.Add(Paragraph($"Undo: {Session.UndoCount} · Redo: {Session.RedoCount}\nRetained source buffers: {Session.RetainedSourceBytes / (1024d * 1024):F1} MiB\nHistory source budget: {Session.HistoryOptions.MaximumSourceBytes / (1024d * 1024):F0} MiB", 11));
        content.Children.Add(new PdfCommandButton("Clear undo history", PdfIconKind.Trash, () => Run(async () =>
        {
            var context = _active; var revision = context.Session.Revision;
            if (await _dialogs.ConfirmAsync("Clear undo and redo history?", "Releases historical source buffers. Your current document is unchanged, but these edits can no longer be undone. This does not save, redact or sanitize the document.", "Clear history") && _active == context)
            {
                if (context.Session.Revision != revision) throw new InvalidOperationException("The document changed while confirming. Review its changes before clearing history.");
                context.Session.ClearHistory(); RefreshRight(); ShowStatus("Undo history released. Current document and save state unchanged.");
            }
        })) { IsEnabled = Session.CanUndo || Session.CanRedo });
        if (Session.SelectedAnnotation is { } annotation)
        {
            content.Children.Add(PdfTheme.Divider()); content.Children.Add(PdfTheme.Text("ANNOTATION", 10, "#777777", true)); content.Children.Add(Paragraph(annotation.Kind.ToString(), 14, "#333333"));
            var palette = new PdfColorPalette(annotation.Color); palette.ColorChanged += color => Safe(() => Session.UpdateAnnotation(annotation.Id, a => a with { Color = color })); content.Children.Add(palette);
            var size = new PdfTextField("Annotation font size") { Text = annotation.FontSize.ToString(CultureInfo.InvariantCulture) };
            var stroke = new PdfTextField("Annotation stroke width") { Text = annotation.StrokeWidth.ToString(CultureInfo.InvariantCulture) };
            content.Children.Add(Paragraph("Font size / Stroke width", 11)); content.Children.Add(size); content.Children.Add(stroke);
            content.Children.Add(new PdfCommandButton("Apply appearance", PdfIconKind.Check, () => Safe(() =>
            {
                if (!double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var font) || !double.TryParse(stroke.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) throw new FormatException("Enter valid numbers.");
                Session.UpdateAnnotation(annotation.Id, a => a with { FontSize = Math.Clamp(font, 1, 200), StrokeWidth = Math.Clamp(width, .5, 30) });
            })));
            content.Children.Add(new PdfCommandButton("Edit text or comment", PdfIconKind.Edit, () => Run(EditSelectedAsync)));
            content.Children.Add(new PdfCommandButton("Delete annotation", PdfIconKind.Trash, () => Safe(() => Session.DeleteSelection())));
        }
        content.Children.Add(PdfTheme.Divider()); content.Children.Add(Paragraph("Structured export retains native content and supported interactive objects. Page assembly may lose document-level structures. XFA and signed-document edits are blocked; use the explicit flattened-copy workflow where appropriate.", 11));
    }
    private void ShowHome()
    {
        _home = true; var content = PdfTheme.Column(24); content.Margin = new Thickness(48, 37, 48, 40); content.MaxWidth = 1120; content.HorizontalAlignment = HorizontalAlignment.Stretch;
        content.Children.Add(PdfTheme.Text("Welcome to PdfSpace", 29, "#282828", true)); content.Children.Add(Paragraph("Everything you need to read, review and make a PDF your own.", 15));
        var actions = PdfTheme.Row(20);
        foreach (var (label, subtitle, icon, action) in new (string, string, PdfIconKind, Action)[] { ("Open a PDF", "Start with a file on your device", PdfIconKind.Folder, () => Run(() => OpenAsync())), ("Create a PDF", "A fresh page for your next idea", PdfIconKind.File, () => AddDocument(new PdfWorkspace())), ("Explore the sample", "Try text, drawing and review tools", PdfIconKind.Star, () => AddDocument(SampleDocument.Create(_typeface))) })
        {
            var card = PdfTheme.Column(15); card.Margin = new Thickness(20); card.Children.Add(new PdfIcon { Kind = icon, Color = 0xFFD93830, Width = 31, Height = 31 }); card.Children.Add(PdfTheme.Text(label, 18, bold: true)); card.Children.Add(Paragraph(subtitle));
            actions.Children.Add(new PdfCommandButton(label, action: action) { Content = card, Width = 275, Height = 160, Background = PdfTheme.Brush("#F8F8F8"), BorderBrush = PdfTheme.Brush("#DDDDDD"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        content.Children.Add(new ScrollViewer { Content = actions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        content.Children.Add(PdfTheme.Text("Open documents", 20, bold: true));
        foreach (var document in _documents)
        {
            var row = PdfTheme.Row(15); row.Children.Add(new PdfIcon { Kind = PdfIconKind.File, Color = 0xFFD93830, Width = 25, Height = 25 }); var details = PdfTheme.Column(3); details.Children.Add(PdfTheme.Text(document.Session.Document.Title, 14)); details.Children.Add(Paragraph($"{document.Session.Document.Pages.Length} pages · Local workspace", 11)); row.Children.Add(details);
            content.Children.Add(new PdfCommandButton(document.Session.Document.Title, action: () => Activate(document)) { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 64, BorderBrush = PdfTheme.Brush("#E6E6E6"), BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        _homeHost.Content = new ScrollViewer { Content = content }; UpdateModeVisibility();
    }
}
