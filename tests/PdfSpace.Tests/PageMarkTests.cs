using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using Reader = PdfSpace.Documents.PdfReader;

internal static class PageMarkTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        Directory.CreateDirectory("artifacts/structured");
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        var original = ObjectEditingSample.Create();
        var settings = new PdfPageMarkSettings { HeaderLeft = "Review {date}", FooterCenter = "Page {page} of {pages}", DateText = "2026-09-29" };
        var apply = PdfPageMarks.Apply(original, [0, 1], settings, font);
        check(apply.ChangedPages == 2 && apply.EmbeddedFonts == 1, "page marks share one font across a batch");
        var marks = PdfPageMarks.Read(apply.Workspace);
        check(marks.Count == 2 && marks.All(m => m.Intact), "native page-mark ownership survives serialization");
        PageMarkReadScopeTests.Run(apply.Workspace, settings, font, check, reject);
        check(Reader.ExtractText(apply.Workspace).Contains("Page 1 of 2"), "page numbering is searchable native PDF text");
        check(ReferenceEquals(PdfPageMarks.Apply(apply.Workspace, [0, 1], settings, font).Workspace, apply.Workspace), "identical mark updates preserve workspace identity");
        var removed = PdfPageMarks.Remove(apply.Workspace, [0, 1], PdfPageMarkKind.HeaderFooter, font);
        check(PdfPageMarks.Read(removed.Workspace).Count == 0, "batch removal clears managed marks");
        check(!Reader.ExtractText(removed.Workspace).Contains("Review 2026"), "removal removes actual text not just metadata");
        var session = new EditorSession(original); session.Execute("Page marks", _ => apply.Workspace);
        check(session.UndoCount == 1, "batch marks commit exactly one undo entry");
        session.Undo(); check(ReferenceEquals(original, session.Document), "mark undo restores source snapshot identity");
        session.Redo(); check(ReferenceEquals(apply.Workspace, session.Document), "mark redo restores edited snapshot identity");
        check(apply.Workspace.Pages.Select(p => p.Id).SequenceEqual(original.Pages.Select(p => p.Id)), "mark materialization preserves stable workspace page identities");
        check(ReferenceEquals(PdfPageMarks.Remove(original, [0], PdfPageMarkKind.Watermark, font).Workspace, original), "removing absent managed watermarks is an exact no-op");
        var unicode = settings with { HeaderLeft = "Żółć café", FooterCenter = "{bates}", BatesPrefix = "CASE-", StartNumber = 23 };
        var updated = PdfPageMarks.Apply(apply.Workspace, [1, 0, 1], unicode, font);
        var updatedText = Reader.ExtractText(updated.Workspace);
        check(updatedText.Contains("Żółć café") && updatedText.Contains("CASE-000023") && updatedText.Contains("CASE-000024"), "native Unicode and Bates sequence use sorted distinct pages");
        check(!updatedText.Contains("Review 2026"), "replacing headers removes previous native header text");
        check(PdfPageMarks.Read(updated.Workspace).Count == 2, "header updates do not accumulate content layers");
        var watermark = new PdfPageMarkSettings { Kind = PdfPageMarkKind.Watermark, WatermarkText = "CONFIDENTIAL", FontSize = 32, Opacity = .25, Rotation = -35 };
        var watermarked = PdfPageMarks.Apply(updated.Workspace, [0,1], watermark, font).Workspace;
        check(PdfPageMarks.Read(watermarked).Count == 4 && Reader.ExtractText(watermarked).Contains("CONFIDENTIAL"), "watermarks and headers coexist as searchable native content");
        var withoutWatermark = PdfPageMarks.Remove(watermarked, [0], PdfPageMarkKind.Watermark, font).Workspace;
        check(PdfPageMarks.Read(withoutWatermark).Count == 3 && PdfPageMarks.Read(withoutWatermark).Count(m => m.Settings.Kind == PdfPageMarkKind.HeaderFooter) == 2,
            "selective watermark removal leaves headers and other pages intact");
        var headerless = PdfPageMarks.Remove(watermarked, [0,1], PdfPageMarkKind.HeaderFooter, font).Workspace;
        check(PdfPageMarks.Read(headerless).Count == 2 && PdfPageMarks.Read(headerless).All(m => m.Settings.Kind == PdfPageMarkKind.Watermark), "header removal preserves watermark layers");
        using (var native = PdfDocumentEngine.OpenNative(apply.Workspace.Sources[0].Bytes))
        {
            var fonts = native.Internals.GetAllObjects().OfType<PdfDictionary>().Count(d => d.Elements.GetName("/Subtype") == "/Type0");
            using var old = PdfDocumentEngine.OpenNative(original.Sources[0].Bytes);
            check(fonts - old.Internals.GetAllObjects().OfType<PdfDictionary>().Count(d => d.Elements.GetName("/Subtype") == "/Type0") == 1,
                "serialized multi-page marks contain only one extra Type0 font");
        }
        var repeated = updated.Workspace;
        for (var i = 0; i < 5; i++) repeated = PdfPageMarks.Apply(repeated, [0,1], unicode with { HeaderLeft = "Revision " + i }, font).Workspace;
        check(repeated.Sources[0].Bytes.Length < updated.Workspace.Sources[0].Bytes.Length * 1.15, "repeated mark updates release obsolete resource graphs instead of growing fonts per revision");
        var alias = new EditorSession(original); alias.DuplicatePage();
        var aliased = PdfPageMarks.Apply(alias.Document, [1], settings, font).Workspace;
        check(PdfPageMarks.Read(aliased).Single().PageIndex == 1, "marking one duplicated source page does not mark its alias");
        using (var text = new PdfSpace.Documents.PdfTextReader(aliased))
            check(!string.Join(' ', text.Words(aliased.Pages[0]).Select(w => w.Text)).Contains("Review 2026") && string.Join(' ', text.Words(aliased.Pages[1]).Select(w => w.Text)).Contains("Review 2026"), "duplicate page content arrays remain independently editable");
        foreach (var rotation in new[] {0,90,180,270})
        {
            var cropped = original.UpdatePage(original.Pages[0].Id, p => p with { Crop = new(20,30,500,700), Rotation = rotation });
            var rotated = PdfPageMarks.Apply(cropped, [0], settings, font).Workspace;
            check(rotated.Pages[0].Width == cropped.Pages[0].DisplayWidth && rotated.Pages[0].Height == cropped.Pages[0].DisplayHeight,
                $"page marks normalize visible crop dimensions at {rotation} degrees");
            check(PdfPageMarks.Read(rotated).Single().Intact, $"page mark integrity respects native rotation {rotation}");
            File.WriteAllBytes($"artifacts/structured/marks-rotation-{rotation}.pdf", rotated.Sources[0].Bytes);
        }
        var form = PdfDocumentEngine.CreateFormSample(font);
        var filled = new EditorSession(form); filled.Navigate(5); filled.SetFieldValue(filled.Page.Fields.First(f => f.Kind == PdfFieldKind.Text).Id, "Page mark customer");
        var markedForm = PdfPageMarks.Apply(filled.Document, [5], settings, font).Workspace;
        check(markedForm.FieldCount == filled.Document.FieldCount && markedForm.Pages[5].Fields.Any(f => f.Value == "Page mark customer"), "interactive form values survive native page marks");
        var sensitive = original with { Sources = original.Sources.Select(x => x with { Sensitive = true }).ToArray() };
        check(PdfPageMarks.Apply(sensitive, [0], settings, font).Workspace.IsSensitive, "materialized page marks propagate source sensitivity");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); reject(() => PdfPageMarks.Apply(original, [0], settings, font, cancellation.Token), "cancelled mark batches make no changes");
        }
        reject(() => PdfPageMarks.Apply(original, [-1], settings, font), "mark batches reject invalid page indices");
        reject(() => PdfPageMarks.Apply(original, [], settings, font), "mark batches reject empty ranges");
        reject(() => PdfPageMarks.Apply(original, Enumerable.Range(0,501).ToArray(), settings, font), "mark batch page limit is enforced before native parsing");
        foreach (var invalid in new[] { settings with { FontSize = double.NaN }, settings with { FontSize = 0 }, settings with { Opacity = double.PositiveInfinity }, settings with { Opacity = 0 }, settings with { LeftMargin = -1 }, settings with { Color = 0x00444444 }, settings with { Kind = (PdfPageMarkKind)99 }, settings with { BatesDigits = 2 }, settings with { DateText = null! }, settings with { HeaderLeft = "\uD800" } })
            reject(() => invalid.Validate(), "invalid page-mark settings rejected");
        reject(() => settings.Expand("{unknown}",0,2), "unknown page-mark placeholders are not silently interpreted");
        reject(() => settings.Expand("unclosed {page",0,2), "unclosed page-mark tokens rejected");
        check(settings.Expand("{{{page}}}",0,2) == "{1}", "literal braces remain deterministic");
        check((settings with { NumberStyle = PdfPageNumberStyle.LowerRoman, StartNumber = 9 }).Expand("{page}",0,2) == "ix", "lowercase Roman page numbers");
        check((settings with { NumberStyle = PdfPageNumberStyle.UpperRoman, StartNumber = 3999 }).Expand("{page}",0,2) == "MMMCMXCIX", "uppercase Roman page numbers");
        reject(() => (settings with { NumberStyle = PdfPageNumberStyle.LowerRoman, StartNumber = 4000 }).Expand("{page}",0,2), "Roman page-number overflow rejects the complete batch");
        reject(() => PdfPageMarks.Layout(settings with { HeaderLeft = new string('W',100), HeaderRight = "Right" },0,2,300,400,font), "overlapping header slots rejected before PDF rewriting");
        reject(() => PdfPageMarks.Layout(watermark with { FontSize = 300 },0,2,100,100,font), "oversized watermarks fail explicitly instead of clipping");
        check((settings with { HeaderLeft = "e\u0301" }).Expand("e\u0301",0,1) == "é", "Latin combining input is normalized before glyph layout");
        reject(() => (settings with { HeaderLeft = "مرحبا" }).Validate(), "unsupported complex shaping is explicit");
        using (var native = PdfDocumentEngine.OpenNative(apply.Workspace.Sources[0].Bytes))
        {
            var page = native.Pages[0];
            var manifest = PdfObjects.Dictionary(PdfObjects.Array(page.Elements["/PdfSpacePageMarks"])!.Elements[0])!;
            var formObject = PdfObjects.Dictionary(PdfObjects.Dictionary(page.Resources.Elements["/XObject"])!.Elements[manifest.Elements.GetName("/Resource")])!;
            formObject.Stream.Value = Encoding.ASCII.GetBytes("0 0 10 10 re f\n");
            var tampered = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "changed.pdf");
            check(!PdfPageMarks.Read(tampered)[0].Intact, "altered page-mark resource bytes invalidate ownership");
            reject(() => PdfPageMarks.Remove(tampered,[0],PdfPageMarkKind.HeaderFooter,font), "remove rejects changed marks rather than deleting unrelated edits");
        }
        using (var native = PdfDocumentEngine.OpenNative(apply.Workspace.Sources[0].Bytes))
        {
            var page = native.Pages[0];
            var manifest = PdfObjects.Dictionary(PdfObjects.Array(page.Elements["/PdfSpacePageMarks"])!.Elements[0])!;
            manifest.Elements.SetInteger("/Version",99);
            var future = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "future.pdf");
            reject(() => PdfPageMarks.Read(future), "unknown metadata versions are not overwritten");
        }
        foreach (var key in new[] { "/PdfSpacePageMarks", "/PdfSpaceMarkIsolation" })
        {
            using var native = PdfDocumentEngine.OpenNative(apply.Workspace.Sources[0].Bytes);
            native.Pages[0].Elements.SetString(key, "malformed");
            var malformed = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "malformed.pdf");
            reject(() => PdfPageMarks.Remove(malformed, [0], PdfPageMarkKind.HeaderFooter, font), "malformed mark metadata cannot be silently discarded: " + key);
        }
        using (var native = PdfDocumentEngine.OpenNative(apply.Workspace.Sources[0].Bytes))
        {
            var manifest = PdfObjects.Dictionary(PdfObjects.Array(native.Pages[0].Elements["/PdfSpacePageMarks"])!.Elements[0])!;
            manifest.Elements.SetInteger("/Ordinal", 5);
            var changedNumber = PdfDocumentEngine.Open(PdfDocumentEngine.Bytes(native), "changed-number.pdf");
            check(!PdfPageMarks.Read(changedNumber)[0].Intact, "numbering metadata participates in the ownership checksum");
            reject(() => PdfPageMarks.Apply(changedNumber, [0], settings, font), "altered settings cannot pass the no-op path");
        }
        using (var alternate = SKTypeface.FromFamilyName("DejaVu Serif"))
        {
            var reFonted = PdfPageMarks.Apply(apply.Workspace, [0,1], settings, alternate);
            check(reFonted.ChangedPages == 2 && !ReferenceEquals(reFonted.Workspace, apply.Workspace), "changing the requested font is not an incorrect no-op");
            check(PdfPageMarks.Read(reFonted.Workspace).All(m => m.Intact && m.TypefaceFingerprint != marks[0].TypefaceFingerprint), "changed font identity is retained in native ownership records");
        }
        var behind = PdfPageMarks.Apply(original, [0], watermark with { BehindContent = true }, font).Workspace;
        check(PdfPageMarks.Read(behind).Single().Settings.BehindContent, "behind-content watermark placement persists");
        File.WriteAllBytes("artifacts/structured/marks-behind.pdf", behind.Sources[0].Bytes);
        var scan = PdfImageImporter.CreateScanExample(font);
        var layer = new PdfOcrLayer { Language = "eng", Engine = "test", Words = [new PdfOcrWord { Text = "Reviewed", Bounds = new(40,40,80,20), Confidence = 1 }] };
        scan = scan.UpdatePage(scan.Pages[0].Id, p => p with { Ocr = layer });
        var markedScan = PdfPageMarks.Apply(scan,[0],settings with { HeaderLeft = "Scan title" },font).Workspace;
        var textOnScan = Reader.ExtractText(markedScan);
        check(textOnScan.Contains("Reviewed") && textOnScan.Contains("Scan title"), "reviewed OCR and visible page-mark text remain searchable together");
        check(textOnScan.Split("Reviewed").Length == 2, "old invisible OCR is not duplicated alongside reviewed words");
        var newLayer = markedScan.Pages[0].Ocr! with { Words = [layer.Words[0] with { Text = "Corrected" }] };
        var corrected = markedScan.UpdatePage(markedScan.Pages[0].Id, p => p with { Ocr = newLayer });
        check(Reader.ExtractText(corrected).Contains("Corrected") && !Reader.ExtractText(corrected).Contains("Reviewed"), "reviewed OCR corrections override stale native hidden text");
        File.WriteAllBytes("artifacts/structured/marks-watermarked.pdf", watermarked.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/marks-aliased.pdf", aliased.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/marks-form.pdf", markedForm.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/marks-scan.pdf", markedScan.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/marks-original.pdf", PdfDocumentEngine.Save(original, font).Bytes);
        File.WriteAllBytes("artifacts/structured/marks-added.pdf", PdfDocumentEngine.Save(apply.Workspace, font).Bytes);
        File.WriteAllBytes("artifacts/structured/marks-removed.pdf", PdfDocumentEngine.Save(removed.Workspace, font).Bytes);
    }
}
