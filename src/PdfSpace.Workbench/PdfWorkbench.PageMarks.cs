using System.Globalization;

namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private PdfPageMarkSettings _markSettings = new();
    private string _markRange = "all";

    private void ShowPageMarks(PdfPageMarkKind kind, bool bates = false)
    {
        Viewport.FinishText(true); Viewport.CancelGesture();
        var existing = PdfPageMarks.Read(Session.Document, [Session.CurrentPage]).FirstOrDefault(m => m.PageIndex == Session.CurrentPage && m.Settings.Kind == kind && m.Intact);
        _markSettings = existing?.Settings ?? (kind == PdfPageMarkKind.HeaderFooter ? new() : new()
        { Kind = kind, WatermarkText = "DRAFT", FontSize = 48, Opacity = .2, Rotation = -35 });
        // A shortcut may seed a new preset, but must never silently replace
        // saved numbering templates or prefixes when reopening existing marks.
        if (bates && existing is null) _markSettings = _markSettings with { FooterCenter = "{bates}", BatesPrefix = "DOC-" };
        _markRange = "all";
        UseTool(PdfTool.Hand);
        if (_mode != "Edit") SetMode("Edit");
        _right = "Page marks"; RefreshRight(); AdaptLayout();
    }

    private void BuildPageMarks(StackPanel content)
    {
        var owner = _active; var snapshot = Session.Document; var pageIndex = Session.CurrentPage;
        var draft = _markSettings;
        var watermark = draft.Kind == PdfPageMarkKind.Watermark;
        var title = watermark ? "Watermark" : "Header and footer";
        void Guard()
        {
            if (_active != owner || !ReferenceEquals(snapshot, owner.Session.Document))
                throw new InvalidOperationException("The document changed. Use its current Page marks panel.");
        }
        content.Children.Add(Paragraph(title.ToUpperInvariant(), 11));
        content.Children.Add(Paragraph("Native, searchable text. Updates replace only verified PdfSpace marks, never arbitrary source content.", 11));
        var preview = new PdfPageMarkPreview { Height = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(preview, "Page mark placement preview");
        content.Children.Add(preview);
        var feedback = Paragraph("", 11); content.Children.Add(feedback);
        var readFields = new List<Action>();
        int[] Pages() => PageRange.Parse(_markRange, snapshot.Pages.Length);
        PdfPageMarkSettings Settings()
        {
            foreach (var read in readFields) read();
            return draft.Validate();
        }
        void Preview()
        {
            try
            {
                var settings = Settings(); var indices = Pages().Order().ToArray();
                if (indices.Length > PdfPageMarks.MaximumPagesPerOperation) throw new ArgumentException("Choose at most 500 pages.");
                var shown = indices.Contains(pageIndex) ? pageIndex : indices[0]; var page = snapshot.Pages[shown];
                preview.Update(settings, Array.IndexOf(indices, shown), snapshot.Pages.Length, page.DisplayWidth, page.DisplayHeight, _typeface);
                _markSettings = settings;
                feedback.Text = $"Placement only · page {shown + 1} · {indices.Length} selected. Source content is not shown.";
                feedback.Foreground = PdfTheme.Brush("#686868");
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidDataException or NotSupportedException or OverflowException)
            { preview.Clear(); feedback.Text = ex.Message; feedback.Foreground = PdfTheme.Brush("#B12620"); }
        }
        void Text(string name, string value, Action<string> set)
        {
            content.Children.Add(Paragraph(name, 10));
            var field = new PdfTextField(name) { Text = value, MaxLength = 1024 };
            readFields.Add(() => set(field.Text));
            field.TextChanged += (_, _) => Preview(); content.Children.Add(field);
        }
        void Number(string name, double value, Action<double> set)
        {
            Text(name, value.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                    throw new ArgumentException(name + " must be a finite number.");
                set(number);
            });
        }
        void Integer(string name, int value, Action<int> set) => Text(name, value.ToString(CultureInfo.InvariantCulture), text =>
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException(name + " must be an integer.");
            set(number);
        });
        Text("Mark page range", _markRange, value => _markRange = value);
        content.Children.Add(Paragraph("Use all or ranges such as 1, 3-5. Numbers advance in ascending selected-page order; {pages} is the document page count.", 10));
        if (watermark) Text("Watermark text", draft.WatermarkText, value => draft = draft with { WatermarkText = value });
        else
        {
            Text("Header left", draft.HeaderLeft, value => draft = draft with { HeaderLeft = value });
            Text("Header center", draft.HeaderCenter, value => draft = draft with { HeaderCenter = value });
            Text("Header right", draft.HeaderRight, value => draft = draft with { HeaderRight = value });
            Text("Footer left", draft.FooterLeft, value => draft = draft with { FooterLeft = value });
            Text("Footer center", draft.FooterCenter, value => draft = draft with { FooterCenter = value });
            Text("Footer right", draft.FooterRight, value => draft = draft with { FooterRight = value });
        }
        content.Children.Add(Paragraph("Tokens: {page}, {pages}, {bates}, {date}. Double braces escape a literal brace.", 10));
        Number("Mark font size", draft.FontSize, value => draft = draft with { FontSize = value });
        Number("Mark opacity percent", draft.Opacity * 100, value => draft = draft with { Opacity = value / 100 });
        if (watermark) Number("Watermark rotation", draft.Rotation, value => draft = draft with { Rotation = value });
        else
        {
            Number("Mark left margin", draft.LeftMargin, value => draft = draft with { LeftMargin = value });
            Number("Mark right margin", draft.RightMargin, value => draft = draft with { RightMargin = value });
            Number("Mark top margin", draft.TopMargin, value => draft = draft with { TopMargin = value });
            Number("Mark bottom margin", draft.BottomMargin, value => draft = draft with { BottomMargin = value });
        }
        var palette = new PdfColorPalette(draft.Color); palette.ColorChanged += value => { draft = draft with { Color = value | 0xFF000000 }; Preview(); };
        content.Children.Add(palette);
        var behind = new PdfCommandButton("Mark behind content");
        behind.Label = draft.BehindContent ? "Behind content · on" : "Behind content · off";
        behind.Click += (_, _) => { draft = draft with { BehindContent = !draft.BehindContent }; behind.Label = draft.BehindContent ? "Behind content · on" : "Behind content · off"; Preview(); };
        content.Children.Add(behind);
        Integer("Mark starting number", draft.StartNumber, value => draft = draft with { StartNumber = value });
        var style = new PdfCommandButton("Mark numbering style");
        style.Label = "Number style · " + draft.NumberStyle;
        style.Click += (_, _) => { draft = draft with { NumberStyle = (PdfPageNumberStyle)(((int)draft.NumberStyle + 1) % 3) }; style.Label = "Number style · " + draft.NumberStyle; Preview(); };
        content.Children.Add(style);
        Integer("Bates digits", draft.BatesDigits, value => draft = draft with { BatesDigits = value });
        Text("Bates prefix", draft.BatesPrefix, value => draft = draft with { BatesPrefix = value });
        Text("Bates suffix", draft.BatesSuffix, value => draft = draft with { BatesSuffix = value });
        Text("Mark date text", draft.DateText, value => draft = draft with { DateText = value });
        var apply = new PdfCommandButton("Apply native page marks", PdfIconKind.Check, () => Run(async () =>
        {
            Guard(); var settings = Settings(); var pages = Pages();
            if (!await _dialogs.ConfirmAsync("Apply page marks?", $"Add or replace {title.ToLowerInvariant()} on {pages.Length} page(s). This rewrites the PDF, retains supported forms/text and is one undoable edit. Signed/XFA and unsafe form reassembly remain blocked. Existing content is not shrunk to make room.", "Apply marks")) return;
            Guard(); ShowStatus("Writing native page marks…"); await Task.Yield(); Guard();
            var result = PdfPageMarks.Apply(snapshot, pages, settings, _typeface);
            owner.Session.Execute("Apply " + title.ToLowerInvariant(), current => ReferenceEquals(current, snapshot) ? result.Workspace : throw new InvalidOperationException("The document changed."));
            owner.Viewport.Navigate(Math.Min(pageIndex, result.Workspace.Pages.Length - 1));
            ShowStatus(result.ChangedPages == 0 ? "Page marks already match. No changes." : $"Native page marks applied to {result.ChangedPages} pages · {result.EmbeddedFonts} shared embedded font. Export PDF to save a copy." +
                (result.Warnings.Count == 0 ? "" : " " + string.Join(" ", result.Warnings)));
        }));
        apply.Primary(); content.Children.Add(apply);
        content.Children.Add(new PdfCommandButton("Remove native page marks", PdfIconKind.Trash, () => Run(async () =>
        {
            Guard(); var pages = Pages();
            if (!await _dialogs.ConfirmAsync("Remove page marks?", $"Remove only verified PdfSpace {title.ToLowerInvariant()} marks in the selected range. Other text, annotations and watermarks of a different kind stay unchanged. This is not secure redaction.", "Remove marks")) return;
            Guard(); var result = PdfPageMarks.Remove(snapshot, pages, draft.Kind, _typeface);
            owner.Session.Execute("Remove " + title.ToLowerInvariant(), current => ReferenceEquals(current, snapshot) ? result.Workspace : throw new InvalidOperationException("The document changed."));
            ShowStatus(result.ChangedPages == 0 ? "No matching PdfSpace marks in this range." : $"Native page marks removed from {result.ChangedPages} pages." +
                (result.Warnings.Count == 0 ? "" : " " + string.Join(" ", result.Warnings)));
        })));
        content.Children.Add(new PdfCommandButton("Reload saved mark settings", PdfIconKind.Undo, () => Safe(() => { Guard(); ShowPageMarks(draft.Kind); })));
        content.Children.Add(Paragraph("Owned settings survive save/reopen. Marks altered with the native object editor cannot be updated through this panel. Adobe/third-party marks are not auto-detected. Behind-content text may be hidden by opaque page content. Complex shaping and image watermarks are not supported here.", 10));
        Preview();
    }
}
