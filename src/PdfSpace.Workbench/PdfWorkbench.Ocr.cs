using PdfSpace.Ocr;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private CancellationTokenSource? _ocrCancellation;
    private DocumentContext? _ocrContext;
    private string _ocrLanguage = "eng";
    private int _ocrDpi = 200;
    private bool _ocrReviewAll = true;
    private int _ocrReviewIndex;
    private PdfCommandButton? _ocrCancelButton;
    private TextBlock? _ocrProgress;

    private void BuildOcrTools()
    {
        _leftPanel.Description("Turn scanned pages into selectable, searchable text. The original scan pixels stay unchanged.");
        _leftPanel.Add("Recognize current page", PdfIconKind.Text, () => Run(() => RecognizeAsync([Session.CurrentPage])), 0xFF1473E6);
        _leftPanel.Add("Recognize pages…", PdfIconKind.Pages, () => Run(async () =>
        {
            var context = _active;
            var value = await _dialogs.PromptAsync("Recognize text", "Choose page numbers or ranges, for example 1, 3-5. Pages with existing non-PdfSpace text are skipped. Up to 100 pages per operation.", "all", acceptLabel: "Recognize");
            if (value is not null && _active == context) await RecognizeAsync(PageRange.Parse(value, Session.Document.Pages.Length));
        }));
        _leftPanel.Add("Correct recognized text", PdfIconKind.Edit, () => { _ocrReviewAll = true; _ocrReviewIndex = 0; ShowOcrReview(); });
        _ocrCancelButton = _leftPanel.Add("Cancel recognition", PdfIconKind.Close, () => _ocrCancellation?.Cancel());
        _ocrCancelButton.IsEnabled = _ocrCancellation is not null;
        _ocrProgress = Paragraph(_ocrCancellation is null ? "Ready to recognize" : "Recognition in progress", 11);
        _ocrProgress.Margin = new Thickness(8); _leftPanel.Items.Children.Add(_ocrProgress);
        _leftPanel.Heading("RECOGNITION SETTINGS");
        var language = _leftPanel.Add("OCR language", PdfIconKind.Text, () =>
        {
            _ocrLanguage = _ocrLanguage switch { "eng" => "pol", "pol" => "deu", _ => "eng" }; BuildLeft();
        });
        language.Label = "Language · " + (_ocrLanguage switch { "pol" => "Polish", "deu" => "German", _ => "English" });
        var resolution = _leftPanel.Add("OCR resolution", PdfIconKind.Image, () => { _ocrDpi = _ocrDpi switch { 150 => 200, 200 => 300, _ => 150 }; BuildLeft(); });
        resolution.Label = $"Resolution · {_ocrDpi} DPI";
        _leftPanel.Description("Rotate or crop before recognition when needed. English, Polish and German models run locally. Handwriting and complex layouts may need substantial correction.");
        _leftPanel.Items.Children.Add(PdfTheme.Divider());
        _leftPanel.Add("Create PDF from image", PdfIconKind.Image, () => Run(async () =>
        {
            var file = await _storage.OpenImageAsync(); if (file is null) return;
            AddDocument(PdfImageEditor.OpenImage(file.Bytes, file.Name));
            FitOcrPageWhenReady(Viewport); ShowStatus("Image imported at 150 DPI. Run Recognize current page to add searchable text.");
        }));
        _leftPanel.Add("Open scanned example", PdfIconKind.File, () =>
        {
            AddDocument(PdfImageImporter.CreateScanExample(_typeface)); FitOcrPageWhenReady(Viewport);
            ShowStatus("This synthetic scan contains no PDF text. Run recognition to make it searchable.");
        });
        _leftPanel.Add("Export searchable PDF", PdfIconKind.Export, () => Run(() => ExportPdfAsync()));
        _leftPanel.Add("Export recognized text", PdfIconKind.Text, () => Run(ExportTextAsync));
        _leftPanel.Description("Review recognition before sharing. PDF export embeds an invisible Unicode text layer; it does not alter the scanned image or certify accessibility.");
    }
    private static void FitOcrPageWhenReady(PdfViewport viewport)
    {
        // Newly attached tabs have no arranged size yet. Wait for real geometry,
        // rather than calculating a fit from the viewport's zero-size fallback.
        if (viewport.ActualWidth > 0 && viewport.ActualHeight > 0) { viewport.FitPage(); return; }
        void Fit(object sender, SizeChangedEventArgs args)
        {
            if (viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0) return;
            viewport.SizeChanged -= Fit;
            viewport.FitPage();
        }
        viewport.SizeChanged += Fit;
    }
    private async Task RecognizeAsync(int[] indices)
    {
        if (_ocrCancellation is not null) { ShowStatus("Recognition is already running. Cancel it before starting another batch."); return; }
        Viewport.FinishText(true); var context = _active; var snapshot = context.Session.Document;
        foreach (var source in snapshot.Sources)
        {
            var inspection = PdfDocumentEngine.Inspect(source.Bytes);
            if (inspection.SignatureFields > 0 || inspection.HasXfa) throw new NotSupportedException("OCR editing is blocked for signed/certified or XFA PDFs. Use a separately created scan copy.");
        }
        using var cancellation = new CancellationTokenSource(); _ocrCancellation = cancellation; _ocrContext = context;
        if (_ocrCancelButton is not null) _ocrCancelButton.IsEnabled = true;
        StateChanged?.Invoke();
        try
        {
            var progress = new Progress<OcrProgress>(item =>
            {
                if (_ocrCancellation != cancellation || _disposed) return;
                var message = $"{item.Stage}: page {item.PageIndex + 1} · {item.Completed}/{item.Total}";
                if (_ocrProgress is not null) _ocrProgress.Text = message;
                ShowStatus(message);
            });
            var batch = await OcrBatch.RecognizeAsync(snapshot, context.Viewport.Renderer, _ocr, indices, _ocrLanguage, _ocrDpi, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_documents.Contains(context)) throw new OperationCanceledException(cancellation.Token);
            context.Session.Execute("Recognize scanned text", current => batch.Apply(current));
            _searchResults = []; // Previous result coordinates/text may refer to a replaced layer.
            if (_active == context && batch.WordCount > 0) { _ocrReviewIndex = 0; ShowOcrReview(); }
            ShowStatus($"Recognized {batch.WordCount} words on {batch.RecognizedPages} pages; skipped {batch.SkippedPages} pages with existing text. Review before exporting.");
            if (_ocrProgress is not null) _ocrProgress.Text = $"{batch.WordCount} words recognized";
        }
        catch (OperationCanceledException) { ShowStatus("Recognition cancelled. No partial OCR results were applied."); if (_ocrProgress is not null) _ocrProgress.Text = "Cancelled"; }
        finally
        {
            _ocrCancellation = null; _ocrContext = null;
            if (_ocrCancelButton is not null) _ocrCancelButton.IsEnabled = false;
            StateChanged?.Invoke();
        }
    }
    private (int Page, PdfOcrWord Word)[] ReviewWords() => Session.Document.Pages
        .SelectMany((page, index) => (page.Ocr?.Words ?? []).Select(word => (Page: index, Word: word)))
        .Where(item => _ocrReviewAll || (!item.Word.Reviewed && item.Word.Confidence < 85)).ToArray();
    private void ShowOcrReview()
    {
        _right = "Recognized text"; var words = ReviewWords();
        _ocrReviewIndex = Math.Clamp(_ocrReviewIndex, 0, Math.Max(0, words.Length - 1));
        if (words.Length > 0)
        {
            var item = words[_ocrReviewIndex];
            Viewport.HighlightSearch(new SearchResult(item.Page, item.Word.Text, item.Word.Bounds));
        }
        RefreshRight(); AdaptLayout();
    }
    private void BuildOcrReview(StackPanel content)
    {
        var words = ReviewWords();
        content.Children.Add(Paragraph("The highlighted word is the original scan. Correcting recognition changes its searchable text, not its pixels.", 12));
        var filter = new PdfCommandButton("Review low-confidence words", PdfIconKind.Search, () => { _ocrReviewAll = !_ocrReviewAll; _ocrReviewIndex = 0; ShowOcrReview(); });
        filter.Label = _ocrReviewAll ? "Show low-confidence words" : "Show all words"; content.Children.Add(filter);
        if (words.Length == 0) { content.Children.Add(Paragraph(_ocrReviewAll ? "No recognized words. Run Scan & OCR first." : "No unreviewed words below 85% confidence. High confidence is not proof of correctness.")); return; }
        _ocrReviewIndex = Math.Clamp(_ocrReviewIndex, 0, words.Length - 1);
        var item = words[_ocrReviewIndex]; var word = item.Word; var context = _active; var pageId = Session.Document.Pages[item.Page].Id;
        content.Children.Add(PdfTheme.Text($"Word {_ocrReviewIndex + 1} of {words.Length}", 16, bold: true));
        content.Children.Add(Paragraph($"Page {item.Page + 1} · Confidence {word.Confidence:F0}%" + (word.Reviewed ? " · Reviewed" : ""), 11));
        content.Children.Add(PdfTheme.Text("Recognized as", 12, bold: true));
        var field = new PdfTextField("Recognized as") { Text = word.Text, MaxLength = 512 }; content.Children.Add(field);
        content.Children.Add(new PdfCommandButton("Accept correction", PdfIconKind.Check, () => Safe(() =>
        {
            if (_active != context) return;
            context.Session.CorrectOcrWord(pageId, word.Id, field.Text);
            _searchResults = []; ShowStatus("Recognition corrected. Original scan pixels are unchanged."); ShowOcrReview();
        })));
        var navigation = PdfTheme.Row(4);
        navigation.Children.Add(new PdfCommandButton("Previous recognized word", PdfIconKind.Left, () => { _ocrReviewIndex = (_ocrReviewIndex + words.Length - 1) % words.Length; ShowOcrReview(); }, true));
        navigation.Children.Add(new PdfCommandButton("Next recognized word", PdfIconKind.Right, () => { _ocrReviewIndex = (_ocrReviewIndex + 1) % words.Length; ShowOcrReview(); }, true)); content.Children.Add(navigation);
        content.Children.Add(PdfTheme.Divider()); content.Children.Add(Paragraph("Recognized words · current page", 11));
        foreach (var (candidate, index) in words.Select((value, index) => (value, index)).Where(pair => pair.value.Page == item.Page).Take(150))
        {
            var button = new PdfCommandButton($"Recognized word {index + 1}", action: () => { _ocrReviewIndex = index; ShowOcrReview(); }) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Label = candidate.Word.Text; button.Select(index == _ocrReviewIndex); content.Children.Add(button);
        }
    }
}
