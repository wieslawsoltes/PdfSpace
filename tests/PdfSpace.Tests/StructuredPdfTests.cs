using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;
using NativeReader = PdfSharp.Pdf.IO.PdfReader;
using WorkspaceReader = PdfSpace.Documents.PdfReader;

internal static class StructuredPdfTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var demo = SampleDocument.Create();
        using (var native = NativeReader.Open(new MemoryStream(demo.Sources[0].Bytes), PdfDocumentOpenMode.Modify))
        {
            native.Internals.Catalog.Elements.SetString("/PdfSpaceSentinel", "PRESERVE CATALOG OBJECTS");
            using var stream = new MemoryStream(); native.Save(stream, false);
            demo = PdfDocumentEngine.Open(stream.ToArray(), "source.pdf");
        }
        var runs = PdfTextEditor.Read(demo, 0);
        var titleRun = runs.First(run => run.Editable && run.Text.Contains("Good ideas", StringComparison.Ordinal));
        var editedText = PdfTextEditor.Replace(demo, titleRun, titleRun.Text.Replace("ideas", "deeds", StringComparison.Ordinal));
        check(WorkspaceReader.ExtractText(editedText).Contains("Good deeds", StringComparison.Ordinal), "actual source text-showing operand is replaced");
        check(!WorkspaceReader.ExtractText(editedText).Contains("Good ideas", StringComparison.Ordinal), "original replaced text is absent from content extraction");
        reject(() => PdfTextEditor.Replace(editedText, titleRun, "stale"), "stale text-run edits are rejected");
        reject(() => PdfTextEditor.Replace(demo, titleRun, "🐙"), "unavailable embedded-font glyph is rejected without a white overlay");
        var textSession = new EditorSession(demo);
        textSession.Execute("Edit original text", _ => editedText); textSession.Undo();
        check(ReferenceEquals(textSession.Document, demo), "native source-text editing is undoable");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/content-edited.pdf", PdfDocumentEngine.Save(editedText, SKTypeface.Default).Bytes);
        var editor = new EditorSession(demo);
        foreach (var kind in new[] { AnnotationKind.Rectangle, AnnotationKind.Ellipse, AnnotationKind.Text, AnnotationKind.Highlight, AnnotationKind.Underline, AnnotationKind.Strikeout, AnnotationKind.Note, AnnotationKind.Arrow, AnnotationKind.Ink, AnnotationKind.Stamp })
        {
            editor.AddAnnotation(new Annotation { Kind = kind, Bounds = new(75, 330 + (int)kind * 17, 120, 15), Points = kind is AnnotationKind.Arrow or AnnotationKind.Ink ? [new(75, 420), new(100, 443), new(150, 428)] : [], Text = kind + " review", Color = 0xFF1473E6 });
        }
        editor.Reply(editor.Page.Annotations[^1].Id, "A retained reply");
        editor.AddAnnotation(new Annotation { Kind = AnnotationKind.Link, Bounds = new(400, 200, 100, 30), Uri = "https://example.com" });
        var saved = PdfDocumentEngine.Save(editor.Document, SKTypeface.Default);
        check(saved.PreservedSourceCatalog, "structured save retains a source catalog");
        using (var native = NativeReader.Open(new MemoryStream(saved.Bytes), PdfDocumentOpenMode.Modify))
        {
            check(native.Internals.Catalog.Elements.GetString("/PdfSpaceSentinel") == "PRESERVE CATALOG OBJECTS", "unknown catalog dictionary survives");
            using var original = NativeReader.Open(new MemoryStream(demo.Sources[0].Bytes), PdfDocumentOpenMode.Modify);
            check(native.Pages[0].Contents.CreateSingleContent().Stream.Value.SequenceEqual(original.Pages[0].Contents.CreateSingleContent().Stream.Value), "source content stream is not rasterized or rewritten by annotation saving");
            check(native.Pages[0].Annotations.Count == 12, "annotations and reply written as native PDF objects");
        }
        var reopened = PdfDocumentEngine.Open(saved.Bytes, "reviewed.pdf");
        check(reopened.AnnotationCount == 11, "standard annotations reimport as editable objects");
        check(reopened.Pages[0].Annotations.Sum(a => a.Replies.Length) == 1, "PDF reply relationship round-trip");
        check(reopened.Pages[0].Annotations.Any(a => a.Kind == AnnotationKind.Link && a.Uri == "https://example.com"), "safe URI link round-trip");
        var savedAgain = PdfDocumentEngine.Save(reopened, SKTypeface.Default);
        check(PdfDocumentEngine.Open(savedAgain.Bytes, "twice.pdf").AnnotationCount == 11, "repeated save does not duplicate annotations");
        var remove = new EditorSession(reopened); remove.Navigate(0); remove.Select(remove.Page.Annotations[0].Id); remove.DeleteSelection();
        check(PdfDocumentEngine.Open(PdfDocumentEngine.Save(remove.Document, SKTypeface.Default).Bytes, "deleted.pdf").AnnotationCount == 10, "deleted imported annotation is removed from native PDF");
        using var renderer = new PdfRenderer();
        check(renderer.ExportPng(reopened, 0).Length > 1000, "native annotations and preview source render");

        var forms = PdfDocumentEngine.CreateFormSample(SKTypeface.Default);
        check(forms.FieldCount == 4, "AcroForm widgets import with field models");
        var formEditor = new EditorSession(forms); formEditor.Navigate(5);
        var text = formEditor.Page.Fields.First(f => f.Name == "FullName");
        formEditor.SetFieldValue(text.Id, "Ada Lovelace");
        var checkbox = formEditor.Page.Fields.First(f => f.Kind == PdfFieldKind.CheckBox);
        formEditor.SetFieldValue(checkbox.Id, checkbox.ExportValue);
        var choice = formEditor.Page.Fields.First(f => f.Kind == PdfFieldKind.ComboBox);
        formEditor.SetFieldValue(choice.Id, "Engineering");
        reject(() => formEditor.SetFieldValue(choice.Id, "Not an option"), "invalid choice value is rejected");
        reject(() => formEditor.SetFieldValue(text.Id, new string('x', 81)), "AcroForm MaxLen enforced");
        var filled = PdfDocumentEngine.Save(formEditor.Document, SKTypeface.Default);
        var filledAgain = PdfDocumentEngine.Open(filled.Bytes, "filled.pdf");
        check(filledAgain.Pages[5].Fields.Single(f => f.Name == "FullName").Value == "Ada Lovelace", "text field value survives native save");
        check(filledAgain.Pages[5].Fields.Single(f => f.Kind == PdfFieldKind.CheckBox).IsChecked, "checkbox V and AS survive native save");
        check(filledAgain.Pages[5].Fields.Single(f => f.Kind == PdfFieldKind.ComboBox).Value == "Engineering", "choice export value survives native save");
        formEditor.Undo(); check(formEditor.Page.Fields.Single(f => f.Kind == PdfFieldKind.ComboBox).Value == "Design", "form filling is undoable");
        formEditor.Redo(); formEditor.ResetForm(); check(formEditor.Page.Fields.Single(f => f.Name == "FullName").Value == "", "reset form restores defaults");
        var serialized = WorkspaceJson.Save(filledAgain);
        check(!serialized.Contains("PreviewBytes", StringComparison.Ordinal), "preview source is not serialized");
        var restored = PdfDocumentEngine.PrepareWorkspace(WorkspaceJson.Load(serialized));
        check(restored.FieldCount == 4 && restored.Sources[0].PreviewBytes is not null, "workspace rehydrates native preview without losing form values");

        var encrypted = PdfDocumentEngine.Save(demo, SKTypeface.Default, new("open-password-123", "owner-password-456", AllowCopy: false));
        reject(() => PdfDocumentEngine.Open(encrypted.Bytes, "protected.pdf"), "encrypted PDF refuses an absent password");
        reject(() => PdfDocumentEngine.Open(encrypted.Bytes, "protected.pdf", "incorrect"), "encrypted PDF refuses wrong password");
        reject(() => PdfDocumentEngine.Open(encrypted.Bytes, "protected.pdf", "open-password-123"), "owner permissions are not bypassed through import mode");
        var decrypted = PdfDocumentEngine.Open(encrypted.Bytes, "protected.pdf", "owner-password-456");
        check(decrypted.IsSensitive && decrypted.Pages.Length == 6, "owner-authorized decryption marks workspace sensitive");
        check(!WorkspaceJson.Save(decrypted).Contains("owner-password", StringComparison.Ordinal), "password is never written to workspace state");

        var redaction = new EditorSession(demo); redaction.AddAnnotation(new Annotation { Kind = AnnotationKind.RedactionMark, Bounds = new(45, 130, 480, 150) });
        reject(() => PdfDocumentEngine.Save(redaction.Document, SKTypeface.Default), "normal PDF save refuses pending redactions");
        var redacted = RasterRedactor.Export(redaction.Document, renderer, 72);
        check(WorkspaceReader.ExtractText(WorkspaceReader.Open(redacted, "redacted.pdf")).Replace("Page", "").Length < 100, "raster redaction output has no original text layer");
        using (var native = NativeReader.Open(new MemoryStream(redacted), PdfDocumentOpenMode.Modify))
        {
            check(native.Internals.Catalog.Elements.GetString("/PdfSpaceSentinel") == "", "redacted document does not copy original catalog metadata");
            check(native.Pages[0].Annotations.Count == 0, "redacted document has no source annotations");
        }
        reject(() => RasterRedactor.Export(demo, renderer), "redaction requires explicit marks");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/annotations.pdf", saved.Bytes);
        File.WriteAllBytes("artifacts/structured/forms.pdf", forms.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/structured/filled.pdf", filled.Bytes);
        File.WriteAllBytes("artifacts/structured/protected.pdf", encrypted.Bytes);
        File.WriteAllBytes("artifacts/structured/redacted.pdf", redacted);
    }
}
