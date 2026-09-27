using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;
using PdfSpace.Layout;
using PdfSpace.Ocr;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using System.Text;

internal static class OcrTests
{
    private const string Header = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";
    private const string Row = "5\t1\t1\t1\t1\t1\t100\t100\t180\t32\t92.5\tCircular\n";
    public static async Task Run(Action<bool, string> check, Action<Action, string> reject)
    {
        using (var preCancelled = new CancellationTokenSource())
        {
            preCancelled.Cancel();
            var didCancel = false;
            try { await new TesseractProcessEngine("pdfspace-nonexistent-engine").RecognizeTsvAsync(new([], 1, 1, 200), "eng", preCancelled.Token); }
            catch (OperationCanceledException) { didCancel = true; }
            check(didCancel, "pre-cancelled native OCR does not launch an executable");
        }
        var scan = PdfImageImporter.CreateScanExample(SKTypeface.Default);
        check(PdfReader.Words(scan, scan.Pages[0]).Length == 0, "scan example contains pixels and no selectable source text");
        var oversized = scan with { Pages = [scan.Pages[0] with { Ocr = new PdfOcrLayer { Words = Enumerable.Range(0, 3000).Select(_ => new PdfOcrWord { Text = new string('é', 512), Bounds = new(10, 10, 100, 20), Confidence = 90 }).ToArray() } }] };
        reject(() => PdfDocumentEngine.Save(oversized, SKTypeface.Default), "oversized OCR metadata cannot produce a PDF that PdfSpace cannot reopen");
        var engine = new TestEngine(Header + Row);
        using var renderer = new PdfRenderer();
        var batch = await OcrBatch.RecognizeAsync(scan, renderer, engine, [0]);
        check(engine.Calls == 1 && engine.Releases == 1, "recognition uses and releases its provider session");
        check(scan.Pages[0].Ocr is null, "recognition does not mutate the input snapshot");
        var recognized = batch.Apply(scan);
        check(recognized.Pages[0].Ocr?.Words.Single().Text == "Circular", "recognized word attaches to the selected page");
        check(ReferenceEquals(scan.Sources[0].Bytes, recognized.Sources[0].Bytes), "recognition retains original scan bytes by identity");
        reject(() => batch.Apply(scan with { Title = "changed" }), "stale OCR results cannot overwrite another document revision");
        check(PdfReader.Find(recognized, "circular").Count() == 1, "recognized text participates in normal PDF search");
        var editor = new EditorSession(scan); editor.Execute("Recognize", _ => batch.Apply(scan)); editor.Undo(); check(editor.Page.Ocr is null, "entire OCR batch is one undo transaction"); editor.Redo();
        var word = editor.Page.Ocr!.Words[0]; editor.CorrectOcrWord(editor.Page.Id, word.Id, "Zażółć");
        check(editor.Page.Ocr.Words[0].Reviewed && editor.Page.Ocr.Words[0].Text == "Zażółć", "Unicode OCR corrections retain geometry and record review");
        reject(() => editor.CorrectOcrWord(editor.Page.Id, word.Id, ""), "empty OCR corrections fail validation atomically");
        check(editor.Page.Ocr.Words[0].Text == "Zażółć", "invalid correction leaves the prior layer unchanged");
        var roundtrip = WorkspaceJson.Load(WorkspaceJson.Save(editor.Document));
        check(roundtrip.Pages[0].Ocr!.Words[0].Text == "Zażółć", "recognized layers survive source-generated workspace JSON");
        var image = new OcrImage([], 1200, 1600, 200);
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var page = new PdfPageState { Width = 600, Height = 800, Rotation = rotation, Crop = new(40, 60, 400, 600) };
            var decoded = TesseractTsv.Decode(Header + Row, image, page, "eng", "test");
            var displayed = PageGeometry.DisplayBounds(page, decoded.Words[0].Bounds);
            check(Math.Abs(displayed.X - 100 * page.DisplayWidth / 1200) < .001 && Math.Abs(displayed.Y - 100 * page.DisplayHeight / 1600) < .001, $"OCR raster-to-page conversion honors crop and rotation {rotation}");
        }
        reject(() => TesseractTsv.Decode("wrong", image, scan.Pages[0], "eng", "test"), "unknown TSV shape rejected");
        reject(() => TesseractTsv.Decode(Header + Row.Replace("92.5", "NaN"), image, scan.Pages[0], "eng", "test"), "nonfinite OCR confidence rejected");
        reject(() => TesseractTsv.Decode(Header + Row.Replace("180", "18000"), image, scan.Pages[0], "eng", "test"), "OCR boxes outside raster rejected");
        reject(() => TesseractTsv.Decode(Header + Row.Replace("Circular", new string('a', 513)), image, scan.Pages[0], "eng", "test"), "excessive OCR word rejected");
        reject(() => OcrBatch.Raster(new PdfWorkspace(), new PdfPageState { Width = 100000, Height = 100000 }, renderer, 300), "raster allocation is bounded before decoding");
        var cancelled = new CancellationToken(true);
        try { await OcrBatch.RecognizeAsync(scan, renderer, engine, [0], cancellationToken: cancelled); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { check(true, "cancelled recognition produces no batch"); }
        var realText = SampleDocument.Create(); var skipped = await OcrBatch.RecognizeAsync(realText, renderer, engine, [0]);
        check(skipped.SkippedPages == 1 && ReferenceEquals(realText, skipped.Apply(realText)) && engine.Calls == 1, "native text pages are skipped without duplicate OCR or dirty history");
        var multi = new EditorSession(scan); multi.DuplicatePage();
        var failing = new TestEngine(Header + Row) { FailAt = 2 };
        try { await OcrBatch.RecognizeAsync(multi.Document, renderer, failing, [0, 1]); throw new Exception("Expected provider failure"); }
        catch (InvalidOperationException) { check(multi.Document.Pages.All(page => page.Ocr is null) && failing.Releases == 1, "failed second page applies no partial OCR batch and releases provider"); }
        var output = PdfDocumentEngine.Save(editor.Document, SKTypeface.Default).Bytes;
        var reopened = PdfDocumentEngine.Open(output, "ocr.pdf");
        check(reopened.Pages[0].Ocr!.Words[0].Text == "Zażółć", "native OCR metadata imports editable Unicode words");
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(output)) check(pdf.GetPage(1).Text.Contains("Zażółć"), "native OCR PDF contains interoperable searchable Unicode text");
        var output2 = PdfDocumentEngine.Save(reopened, SKTypeface.Default).Bytes;
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(output2)) check(pdf.GetPage(1).Text == "Zażółć", "repeated save replaces owned text instead of duplicating OCR");
        var before = renderer.ExportPng(scan, 0, .5); var after = renderer.ExportPng(reopened, 0, .5);
        using (var b = SKBitmap.Decode(before)) using (var a = SKBitmap.Decode(after)) check(b.Pixels.SequenceEqual(a.Pixels), "invisible OCR text does not change rendered scan pixels");
        for (var rotation = 0; rotation < 360; rotation += 90)
        {
            var rotated = editor.Document with { Pages = [editor.Page with { Rotation = rotation }] };
            var saved = PdfDocumentEngine.Open(PdfDocumentEngine.Save(rotated, SKTypeface.Default).Bytes, "rotated.pdf");
            check(saved.Pages[0].Ocr!.Rotation == (360 - rotation) % 360, $"saved OCR reading direction rebases after page rotation {rotation}");
            var second = PdfDocumentEngine.Open(PdfDocumentEngine.Save(saved, SKTypeface.Default).Bytes, "resaved.pdf");
            check(PdfReader.ExtractText(second).Contains("Zażółć"), $"rotated OCR remains searchable after repeated save {rotation}");
        }
        var empty = reopened with { Pages = [reopened.Pages[0] with { Ocr = reopened.Pages[0].Ocr! with { Words = [] } }] };
        var cleared = PdfDocumentEngine.Save(empty, SKTypeface.Default).Bytes;
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(cleared)) check(pdf.GetPage(1).Text.Length == 0, "cleared owned OCR does not reappear from retained source streams");
        Directory.CreateDirectory("artifacts/engine"); Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/engine/scanned.pdf", scan.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/ocr-searchable.pdf", output);
        File.WriteAllBytes("artifacts/structured/ocr-original.pdf", scan.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/engine/scanned.png", OcrBatch.Raster(scan, scan.Pages[0], renderer, 150).Png);
        if (Environment.GetEnvironmentVariable("PDFSPACE_TEST_NATIVE_OCR") == "1")
        {
            var actual = await OcrBatch.RecognizeAsync(scan, renderer, new TesseractProcessEngine(), [0], dpi: 150);
            var actualDocument = actual.Apply(scan);
            check(PdfReader.ExtractText(actualDocument).Contains("Circular", StringComparison.OrdinalIgnoreCase), "real native Tesseract recognizes the synthetic image-only PDF");
            File.WriteAllBytes("artifacts/structured/ocr-native-recognized.pdf", PdfDocumentEngine.Save(actualDocument, SKTypeface.Default).Bytes);
        }
    }
    private sealed class TestEngine(string response) : IOcrEngine
    {
        public string Name => "Deterministic contract fixture";
        public int Calls { get; private set; }
        public int Releases { get; private set; }
        public int FailAt { get; init; }
        public Task<string> RecognizeTsvAsync(OcrImage image, string language, CancellationToken cancellationToken)
        { Calls++; return Calls == FailAt ? Task.FromException<string>(new InvalidOperationException("Synthetic failure")) : Task.FromResult(response); }
        public ValueTask ReleaseAsync() { Releases++; return ValueTask.CompletedTask; }
    }
}
