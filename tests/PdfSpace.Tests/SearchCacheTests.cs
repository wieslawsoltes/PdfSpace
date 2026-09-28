using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class SearchCacheTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var workspace = NativeObjectSample.Create(); var index = new PdfSearchIndex();
        var first = index.Find(workspace, "image").ToArray();
        check(first.Select(hit => (hit.PageIndex, hit.Text)).SequenceEqual(PdfReader.Find(workspace, "image").Select(hit => (hit.PageIndex, hit.Text))), "cached search matches existing text search");
        var count = index.PagesIndexed; _ = index.Find(workspace, "native").ToArray();
        check(index.PagesIndexed == count && index.CachedPages == 2, "repeated searches reuse page text and word offsets");
        check(index.Find(workspace, "imag", wholeWord: true).Count() == 0 && index.Find(workspace, "image", wholeWord: true).Any(), "whole-word search rejects partial word matches");
        check(!index.Find(workspace, "IMAGE", matchCase: true).Any() && index.Find(workspace, "IMAGE").Any(), "cached search honors case sensitivity");
        var added = workspace.UpdatePage(workspace.Pages[0].Id, p => p with { Annotations = [new Annotation { Text = "Distinct comment", Kind = AnnotationKind.Note }] });
        check(index.Find(added, "Distinct").Count() == 1 && index.PagesIndexed == count, "annotation-only search reads current comments without reparsing source pages");
        var run = PdfTextEditor.Read(workspace, 0).First(r => r.Text == "Shared image and text");
        var changed = PdfTextEditor.Replace(workspace, run, "Unique original wording");
        check(index.Find(changed, "Unique").Count() == 1 && index.PagesIndexed > count, "native text edits invalidate stale search entries");
        var withOcr = new PdfWorkspace { Pages = [new PdfPageState { Ocr = new PdfOcrLayer { Words = [new PdfOcrWord { Text = "Alpha", Bounds = new(10, 10, 50, 20), Confidence = 90 }] } }] };
        check(index.Find(withOcr, "Alpha").Any(), "search index recognizes managed OCR words");
        var corrected = withOcr.UpdatePage(withOcr.Pages[0].Id, p => p with { Ocr = p.Ocr! with { Words = [p.Ocr.Words[0] with { Text = "Bravo" }] } });
        check(!index.Find(corrected, "Alpha").Any() && index.Find(corrected, "Bravo").Any(), "OCR corrections replace cached recognized words");
        var bounded = new PdfSearchIndex(1024); _ = bounded.Find(workspace, "image").ToArray();
        check(bounded.CachedBytes <= 1024, "oversized search pages cannot exceed the cache budget");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        reject(() => index.Find(workspace, "image", cancellationToken: cancelled.Token).ToArray(), "cancelled indexed search stops before reading pages");
        reject(() => index.Find(workspace, new string('a', 4097)).ToArray(), "search query length is bounded");
        index.Clear(); check(index.CachedPages == 0 && index.CachedBytes == 0, "closing a document releases its text index");
        var sources = new PdfSpace.Editing.EditorSession(workspace); sources.Combine(NativeObjectSample.Create()); sources.Combine(NativeObjectSample.Create());
        using var renderer = new PdfRenderer { CacheCapacity = 4, SourceCacheCapacity = 1 };
        var one = renderer.ExportPng(sources.Document, 0, .3);
        _ = renderer.ExportPng(sources.Document, 2, .3); _ = renderer.ExportPng(sources.Document, 4, .3);
        check(renderer.CachedSourceCount == 1 && renderer.CachedPictureCount == 3, "parsed source and picture caches have independent limits");
        check(renderer.ExportPng(sources.Document, 0, .3).SequenceEqual(one), "cached pictures remain valid after source-parser eviction");
        var assembled = workspace with { Pages = Enumerable.Range(0, 48).Select(i => workspace.Pages[i % 2] with { Id = Guid.NewGuid() }).ToArray() };
        var saved = PdfDocumentEngine.Save(assembled, SKTypeface.Default);
        check(saved.ImportSourceCount == 1 && PdfDocumentEngine.Inspect(saved.Bytes).Pages == 48, "48-page native assembly opens its single import source only once");
    }
}
