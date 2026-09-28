using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using TextReader = PdfSpace.Documents.PdfReader;

internal static class NativeObjectEditingTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var original = NativeObjectSample.Create(); var images = PdfImageEditor.Read(original, 0); var replacement = NativeObjectSample.ReplacementImage();
        check(images.Length == 2 && images.All(i => i.ScopePath.Split('/').Length == 2), "nested shared image occurrences are individually discovered");
        check(images[0].Bounds == new RectD(60, 240, 160, 70), "nested matrices map image bounds into source logical coordinates");
        check(images[0].ScopePath != images[1].ScopePath && images[0].ResourceName == images[1].ResourceName, "occurrence identity is distinct from resource identity");
        using var beforeRenderer = new PdfRenderer(); var before0 = beforeRenderer.ExportPng(original, 0, 1); var before1 = beforeRenderer.ExportPng(original, 1, 1);
        var replaced = PdfImageEditor.Replace(original, images[0], replacement);
        var afterImages = PdfImageEditor.Read(replaced, 0);
        check(afterImages[0].PixelWidth == 100 && afterImages[1].PixelWidth == 40, "image replacement isolates the selected resource occurrence");
        check(PdfImageEditor.Read(replaced, 1).Single().PixelWidth == 40, "image replacement does not alter another page sharing the nested form");
        check(afterImages[0].Bounds == images[0].Bounds, "image replacement preserves the drawing matrix");
        using var afterRenderer = new PdfRenderer();
        check(afterRenderer.ExportPng(replaced, 1, 1).SequenceEqual(before1), "untouched page pixels are identical after image replacement");
        check(RegionEqual(before0, afterRenderer.ExportPng(replaced, 0, 1), images[1].Bounds), "sibling image pixels are identical after copy-on-write editing");
        reject(() => PdfImageEditor.Delete(replaced, images[0]), "stale image selections cannot modify a newer source");
        reject(() => PdfImageEditor.Delete(original, images[0] with { ScopePath = images[1].ScopePath }), "forged occurrence path cannot retarget an image selection");
        reject(() => PdfImageEditor.Replace(original, images[0], Encoding.ASCII.GetBytes("not an image")), "invalid replacement is rejected without mutating the original");
        var moved = PdfImageEditor.SetBounds(original, images[0], images[0].Bounds.Translate(new(18, 12)));
        check(Near(PdfImageEditor.Read(moved, 0)[0].Bounds, new(78, 252, 160, 70)), "native image move updates only its invocation transform");
        check(PdfImageEditor.Read(moved, 0)[1].Bounds == images[1].Bounds, "image move preserves sibling placement");
        var resized = PdfImageEditor.SetBounds(original, images[0], new(60, 240, 80, 35));
        check(Near(PdfImageEditor.Read(resized, 0)[0].Bounds, new(60, 240, 80, 35)), "native image resize persists dimensions");
        var rotated = PdfImageEditor.Rotate(original, images[0], 90); var rb = PdfImageEditor.Read(rotated, 0)[0].Bounds;
        check(Math.Abs(rb.Width - 70) < .01 && Math.Abs(rb.Height - 160) < .01 && rb.Center.Distance(images[0].Bounds.Center) < .01, "native image quarter-turn preserves its center");
        var flipped = PdfImageEditor.Flip(original, images[0], true);
        check(Near(PdfImageEditor.Read(flipped, 0)[0].Bounds, images[0].Bounds), "horizontal image flip preserves its bounds");
        var deleted = PdfImageEditor.Delete(original, images[0]);
        check(PdfImageEditor.Read(deleted, 0).Length == 1 && PdfImageEditor.Read(deleted, 1).Length == 1, "deleting one nested image invocation retains all other occurrences");
        check(TextReader.ExtractText(deleted).Contains("Shared image and text"), "deleting an image does not remove neighboring source text");
        var inserted = PdfImageEditor.Insert(original, 0, replacement, new(310, 50, 150, 90));
        check(PdfImageEditor.Read(inserted, 0).Length == 3 && PdfImageEditor.Read(inserted, 1).Length == 1, "image insertion adds one native XObject invocation");
        var blank = PdfImageEditor.Insert(new PdfWorkspace(), 0, replacement, new(50, 50, 150, 90));
        check(PdfImageEditor.Read(blank, 0).Length == 1 && blank.Pages[0].Width == 595, "image insertion supports a blank workspace page");
        reject(() => PdfImageEditor.SetBounds(original, images[0], new(0, 0, 0, 100)), "zero-width image transform is rejected");
        reject(() => PdfImageEditor.Transform(original, images[0], new(0, 0, 0, 0, 0, 0)), "singular image transform is rejected");
        reject(() => PdfImageEditor.Rotate(original, images[0], double.NaN), "nonfinite image rotation is rejected");
        var history = new EditorSession(original); history.DuplicatePage();
        var duplicateTarget = PdfImageEditor.Read(history.Document, 1)[0]; history.Execute("Replace copied image", doc => PdfImageEditor.Replace(doc, duplicateTarget, replacement));
        check(PdfImageEditor.Read(history.Document, 0)[0].PixelWidth == 40 && PdfImageEditor.Read(history.Document, 1)[0].PixelWidth == 100, "editing a duplicated workspace page does not change the original page");
        history.Undo(); check(PdfImageEditor.Read(history.Document, 1)[0].PixelWidth == 40, "native image editing is one reversible transaction");
        var text = PdfTextEditor.Read(original, 0).Where(r => r.Text == "Shared image and text").ToArray();
        check(text.Length == 2 && text.All(r => r.Editable && r.CanReplaceFont), "nested-form source text is editable per occurrence");
        var edited = PdfTextEditor.Replace(original, text[0], "Unique image and text");
        var pageText = string.Join(" ", TextReader.Words(edited, edited.Pages[0]).Select(word => word.Text));
        check(pageText.Contains("Unique image and text") && pageText.Contains("Shared image and text"), "source text editing isolates nested shared forms");
        check(string.Join(" ", TextReader.Words(edited, edited.Pages[1]).Select(word => word.Text)).Contains("Shared image and text"), "source text editing preserves other pages");
        using var editedRenderer = new PdfRenderer(); check(editedRenderer.ExportPng(edited, 1, 1).SequenceEqual(before1), "untouched page rendering is unchanged after native text edit");
        var unicode = PdfTextEditor.ReplaceWithFont(original, text[0], "Zażółć gęślą jaźń", SKTypeface.Default, 15);
        check(TextReader.ExtractText(unicode).Contains("Zażółć gęślą jaźń"), "replacement-font text is natively searchable Unicode");
        var unicodeAgain = PdfTextEditor.Read(unicode, 0).Single(r => r.Text == "Zażółć gęślą jaźń");
        check(unicodeAgain.Editable && unicodeAgain.CanReplaceFont, "replacement font has a reusable source encoding");
        var repeated = PdfTextEditor.ReplaceWithFont(unicode, unicodeAgain, "Résumé – Prüfung", SKTypeface.Default, 16);
        check(TextReader.ExtractText(repeated).Contains("Résumé – Prüfung") && !TextReader.ExtractText(repeated).Contains("Zażółć"), "repeated font replacement replaces actual text without overlay accumulation");
        reject(() => PdfTextEditor.ReplaceWithFont(original, text[0], "line\nbreak", SKTypeface.Default), "paragraph input is rejected rather than silently mispositioned");
        reject(() => PdfTextEditor.ReplaceWithFont(original, text[0], "invalid", SKTypeface.Default, double.NaN), "replacement font size is validated");
        var duplicateText = new EditorSession(original); duplicateText.DuplicatePage();
        var run = PdfTextEditor.Read(duplicateText.Document, 1).First(r => r.Text == "Shared image and text");
        duplicateText.Execute("Edit duplicated text", doc => PdfTextEditor.Replace(doc, run, "Isolated image and text"));
        check(!string.Join(" ", TextReader.Words(duplicateText.Document, duplicateText.Document.Pages[0]).Select(w => w.Text)).Contains("Isolated"), "text editing isolates duplicated source-page aliases");
        var save = PdfDocumentEngine.Save(replaced, SKTypeface.Default); var reopen = PdfDocumentEngine.Open(save.Bytes, "edited.pdf");
        check(PdfImageEditor.Read(reopen, 0)[0].PixelWidth == 100 && PdfImageEditor.Read(reopen, 1)[0].PixelWidth == 40, "image edit survives structured save and reopen");
        var serialized = WorkspaceJson.Save(unicode); var restored = PdfDocumentEngine.PrepareWorkspace(WorkspaceJson.Load(serialized));
        check(TextReader.ExtractText(restored).Contains("Zażółć"), "native Unicode edits survive workspace JSON round-trip");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/objects-original.pdf", original.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/objects-replaced.pdf", save.Bytes);
        File.WriteAllBytes("artifacts/structured/objects-unicode.pdf", PdfDocumentEngine.Save(unicode, SKTypeface.Default).Bytes);
        File.WriteAllBytes("artifacts/engine/objects.pdf", original.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/engine/replacement.png", replacement);
    }
    private static bool Near(RectD a, RectD b) => Math.Abs(a.X-b.X) < .01 && Math.Abs(a.Y-b.Y) < .01 && Math.Abs(a.Width-b.Width) < .01 && Math.Abs(a.Height-b.Height) < .01;
    private static bool RegionEqual(byte[] before, byte[] after, RectD rect)
    {
        using var a = SKBitmap.Decode(before); using var b = SKBitmap.Decode(after);
        for (var y = (int)rect.Y; y < rect.Bottom; y++) for (var x = (int)rect.X; x < rect.Right; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }
}
