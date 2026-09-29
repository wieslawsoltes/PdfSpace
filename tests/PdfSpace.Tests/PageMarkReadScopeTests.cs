using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class PageMarkReadScopeTests
{
    public static void Run(PdfWorkspace marked, PdfPageMarkSettings settings, SKTypeface font,
        Action<bool, string> check, Action<Action, string> reject)
    {
        PageMarkEnvelopeTests.Run(marked, settings, font, check);
        var selected = PdfPageMarks.Read(marked, [1]);
        check(selected.Count == 1 && selected[0].PageIndex == 1 && selected[0].Intact,
            "page-mark inspection reads only its selected page");
        var ordered = PdfPageMarks.Read(marked, [1, 0, 1]);
        check(ordered.Select(mark => mark.PageIndex).SequenceEqual(new[] { 0, 1 }),
            "page-mark inspection deduplicates and orders requested pages");
        reject(() => PdfPageMarks.Read(marked, [-1]), "page-mark inspection rejects invalid page indices");
        reject(() => PdfPageMarks.Read(marked, []), "page-mark inspection rejects empty page ranges");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        reject(() => PdfPageMarks.Read(marked, [0], cancellation.Token), "page-mark inspection observes cancellation before parsing");

        // Valid workspace framing does not imply a fully parsed source. An unused
        // retained PDF is not an input to mark inspection or an absent-mark no-op.
        var orphan = new PdfSource(Guid.NewGuid(), "unreferenced.pdf", "%PDF-unparsed"u8.ToArray());
        var extra = marked with { Sources = [.. marked.Sources, orphan] };
        check(PdfPageMarks.Read(extra).Count == 2,
            "page-mark inspection does not parse unreferenced retained sources");
        check(ReferenceEquals(PdfPageMarks.Remove(extra, [0], PdfPageMarkKind.Watermark, font).Workspace, extra),
            "absent-mark no-op does not parse unreferenced retained sources");

        using var native = PdfDocumentEngine.OpenNative(marked.Sources[0].Bytes);
        var metadata = PdfObjects.Dictionary(PdfObjects.Array(native.Pages[1].Elements["/PdfSpacePageMarks"])!.Elements[0])!;
        metadata.Elements.SetInteger("/Version", 999);
        var future = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "unselected-future-record.pdf");
        check(PdfPageMarks.Read(future, [0]).Single().Intact,
            "current-page inspection does not interpret unselected future-version metadata");
        reject(() => PdfPageMarks.Read(future), "whole-document inspection still rejects future-version metadata");
        reject(() => PdfPageMarks.Read(future, [1]), "selected future-version metadata is never ignored");
        check(ReferenceEquals(PdfPageMarks.Apply(future, [0], settings, font).Workspace, future),
            "unchanged selected marks do not inspect unrelated future-version records");
    }
}
