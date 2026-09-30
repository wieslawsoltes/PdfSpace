using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class AttachmentEditingTests
{
    private static PdfEmbeddedFileInfo File(PdfWorkspace workspace, string name) =>
        PdfEmbeddedFiles.Read(workspace.Sources.Single().Bytes).Single(f => f.FileName == name);
    private static byte[] Contents(PdfWorkspace workspace, string name) => PdfEmbeddedFiles.Extract(workspace.Sources.Single().Bytes, File(workspace, name));
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var original = NativeObjectSample.Create(); var source = original.Sources.Single();
        var input = new PdfAttachmentInput("raport-Żółć.txt", Encoding.UTF8.GetBytes("Native attachment — original.\n"), "First description", "text/plain");
        var added = PdfAttachmentEditor.Add(original, input);
        var item = File(added, input.FileName);
        check(Contents(added, input.FileName).SequenceEqual(input.Bytes), "attachment authoring saves exact Unicode payload bytes");
        check(item.Description == input.Description && item.MediaType == "text/plain", "attachment description and MIME type persist");
        check(PdfEmbeddedFiles.Read(source.Bytes).Count == 0, "attachment addition never mutates original source bytes");
        check(ReferenceEquals(original.Pages, added.Pages), "attachment-only changes keep the page-state array identity");
        check(added.Sources[0].Id == source.Id && ReferenceEquals(added.Sources[0].PreviewBytes, source.PreviewBytes ?? source.Bytes), "attachment-only changes reuse source/preview identity without renderer import");
        using (var renderer = new PdfRenderer())
        {
            var before = renderer.ExportPng(original, 0);
            var cache = renderer.CachedPictureCount;
            check(renderer.ExportPng(added, 0).SequenceEqual(before) && renderer.CachedPictureCount == cache, "attachment changes reuse cached page pictures with identical rendered pixels");
        }
        using var font = SKTypeface.FromFamilyName("DejaVu Sans");
        var saved = PdfDocumentEngine.Save(added, font).Bytes;
        var reopened = PdfDocumentEngine.Open(saved, "saved.pdf");
        check(Contents(reopened, input.FileName).SequenceEqual(input.Bytes), "normal structured export preserves authored catalog files");
        check(PdfReader.ExtractText(reopened).Contains("Shared image and text"), "attachment authoring retains searchable native page text");
        var session = new EditorSession(original);
        session.Execute("Attach", _ => added); session.Undo();
        check(ReferenceEquals(session.Document, original) && !session.IsDirty, "attachment Undo restores exact original workspace identity");
        session.Redo(); check(ReferenceEquals(session.Document, added), "attachment Redo restores the authored workspace");
        var edited = PdfAttachmentEditor.SetDescription(added, item, "Updated — 日本語");
        var editedItem = File(edited, input.FileName);
        check(editedItem.Description == "Updated — 日本語", "attachment descriptions support validated Unicode");
        check(ReferenceEquals(PdfAttachmentEditor.SetDescription(edited, editedItem, "Updated — 日本語"), edited), "exact attachment description no-op preserves workspace identity");
        var replacement = Encoding.UTF8.GetBytes("Replacement payload\n");
        var replaced = PdfAttachmentEditor.Replace(edited, editedItem, replacement, "text/plain");
        check(Contents(replaced, input.FileName).SequenceEqual(replacement), "native attachment replacement persists actual new payload");
        check(File(replaced, input.FileName).Description == editedItem.Description, "attachment replacement retains name and description");
        var removed = PdfAttachmentEditor.Remove(replaced, File(replaced, input.FileName));
        check(PdfEmbeddedFiles.Read(removed.Sources[0].Bytes).Count == 0, "removing last catalog entry leaves no attachment entries");
        check(ReferenceEquals(removed.Pages, original.Pages), "attachment removal does not re-import page models");
        reject(() => PdfAttachmentEditor.Remove(replaced, item), "stale attachment descriptors reject changed source hashes");
        reject(() => PdfAttachmentEditor.Remove(added, item with { FileName = "forged.txt" }), "matching hash alone does not authorize a forged descriptor");
        check(ReferenceEquals(PdfAttachmentEditor.AddRange(original, []), original), "empty attachment batches preserve document identity");
        var blank = new PdfWorkspace { Pages = [new() { Rotation = 90, Crop = new(10, 10, 500, 700), Label = new() { Style = PdfPageLabelStyle.RomanLower }, LabelInitialized = true }] };
        var blankAdded = PdfAttachmentEditor.Add(blank, input);
        check(blankAdded.Pages[0].Id == blank.Pages[0].Id && blankAdded.Pages[0].Rotation == 90 && blankAdded.Pages[0].Crop == blank.Pages[0].Crop, "blank attachment authoring preserves stable page IDs/crop/rotation");
        check(Contents(PdfDocumentEngine.Open(PdfDocumentEngine.Save(blankAdded, font).Bytes, "blank.pdf"), input.FileName).SequenceEqual(input.Bytes), "new blank document exports real embedded files");
        var note = new Annotation { Kind = AnnotationKind.Note, Bounds = new(50, 50, 24, 24), Text = "Pending review" };
        var annotated = original.UpdatePage(original.Pages[0].Id, p => p with { Annotations = [.. p.Annotations, note] });
        var annotatedAdded = PdfAttachmentEditor.Add(annotated, input);
        check(ReferenceEquals(annotatedAdded.Pages, annotated.Pages) && annotatedAdded.Pages[0].Annotations.Contains(note), "pending workspace review state survives catalog-only edits");
        var form = new EditorSession(PdfDocumentEngine.CreateFormSample(font)); form.Navigate(5);
        form.SetFieldValue(form.Page.Fields.First(f => f.Kind == PdfFieldKind.Text).Id, "Attachment customer");
        var formAdded = PdfAttachmentEditor.Add(form.Document, input);
        check(ReferenceEquals(formAdded.Pages, form.Document.Pages), "pending AcroForm states are not re-imported by attachment edits");
        var formReopened = PdfDocumentEngine.Open(PdfDocumentEngine.Save(formAdded, font).Bytes, "form.pdf");
        check(formReopened.Pages[5].Fields.Any(f => f.Value == "Attachment customer") && Contents(formReopened, input.FileName).SequenceEqual(input.Bytes),
            "attachment export retains the edited AcroForm value and payload");
        var scan = PdfImageImporter.CreateScanExample(font);
        var layer = new PdfOcrLayer { Engine = "attachment test", Words = [new() { Text = "Reviewed", Bounds = new(40, 40, 80, 20), Confidence = 95, Reviewed = true }] };
        scan = scan.UpdatePage(scan.Pages[0].Id, p => p with { Ocr = layer });
        var scannedAdded = PdfAttachmentEditor.Add(scan, input);
        check(ReferenceEquals(scannedAdded.Pages[0].Ocr, layer), "attachment edit retains reviewed OCR layer identity");
        var scanReopened = PdfDocumentEngine.Open(PdfDocumentEngine.Save(scannedAdded, font).Bytes, "scan.pdf");
        check(PdfReader.ExtractText(scanReopened).Contains("Reviewed") && Contents(scanReopened, input.FileName).SequenceEqual(input.Bytes),
            "attachment export retains searchable reviewed OCR and payload");
        var sensitive = original with { Sources = [source with { Sensitive = true }] };
        check(PdfAttachmentEditor.Add(sensitive, input).IsSensitive, "attachment authoring retains source sensitivity");
        var ordered = original with { Pages = original.Pages.Reverse().ToArray() };
        reject(() => PdfAttachmentEditor.Add(ordered, input), "reordered original catalogs are not silently rewritten for attachment authoring");
        reject(() => PdfAttachmentEditor.Add(WorkspaceComposition.Append(original, original), input), "combined source catalogs require explicit export/reopen before authoring");
        reject(() => PdfAttachmentEditor.Add(original.UpdatePage(original.Pages[0].Id, p => p with { Annotations = [new() { Kind = AnnotationKind.RedactionMark, Bounds = new(1, 1, 30, 30) }] }), input), "pending-redaction documents cannot bypass structured edit guards");
        foreach (var key in new[] { "/Perms", "/Collection" })
            reject(() => PdfAttachmentEditor.Add(ChangeSource(original, n => n.Internals.Catalog.Elements[key] = new PdfDictionary(n)), input), "attachment edit rejects unsupported catalog " + key);
        reject(() => PdfAttachmentEditor.Add(ChangeSource(original, n => PdfObjects.DictionaryValue(n.Internals.Catalog, "/AcroForm").Elements.SetString("/XFA", "data")), input), "attachment edits reject XFA");
        reject(() => PdfAttachmentEditor.Add(ChangeSource(original, n =>
        {
            var direct = new PdfDictionary(n); direct.Elements.SetName("/FT", "/Sig");
            PdfObjects.DictionaryValue(n.Internals.Catalog, "/AcroForm").Elements["/Fields"] = new PdfArray(n, direct);
        }), input), "unattached direct signature fields cannot evade attachment edit guards");
        reject(() => PdfAttachmentEditor.Add(ChangeSource(original, n =>
        {
            var names = new PdfDictionary(n); names.CreateStream([1, 2, 3]); n.Internals.AddObject(names);
            n.Internals.Catalog.Elements["/Names"] = names.Reference!;
        }), input), "streamed catalog Names dictionaries cannot be normalized into attachment catalogs");
        var halfBudget = input with { Bytes = new byte[PdfEmbeddedFiles.MaximumExtractedBytes] };
        reject(() => PdfAttachmentEditor.AddRange(original, [halfBudget, halfBudget, input]), "attachment batch enforces cumulative 32 MiB input limit");
        reject(() => PdfAttachmentEditor.AddRange(original, [input, input with { FileName = "../invalid" }]), "invalid later attachment rejects the entire add batch");
        check(PdfEmbeddedFiles.Read(source.Bytes).Count == 0, "rejected multi-file batch never modifies the source");
        foreach (var name in new[] { "../x.txt", @"a\x.txt", "hidden\u202E.txt", "", " space.txt", "dot.", new string('a', 121), "broken\ud800" })
            reject(() => PdfAttachmentEditor.Add(original, input with { FileName = name }), "attachment authoring rejects invalid filename " + name.Length);
        reject(() => PdfAttachmentEditor.Add(original, input with { Bytes = new byte[PdfEmbeddedFiles.MaximumExtractedBytes + 1] }), "attachment input obeys 16 MiB per-file budget");
        reject(() => PdfAttachmentEditor.AddRange(original, Enumerable.Repeat(input, 33).ToArray()), "attachment batch obeys file-count limit");
        reject(() => PdfAttachmentEditor.Add(original, input with { MediaType = "text/plain; active=yes" }), "attachment MIME parameters cannot inject PDF name content");
        reject(() => PdfAttachmentEditor.SetDescription(added, item, new string('x', 4097)), "attachment description limit is enforced before mutation");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        reject(() => PdfAttachmentEditor.Add(original, input, cancel.Token), "attachment add observes precancellation");
        reject(() => PdfAttachmentEditor.Remove(added, item, cancel.Token), "attachment remove observes precancellation");
        var empty = PdfAttachmentEditor.Add(original, input with { FileName = "empty.bin", Bytes = [] });
        check(Contents(empty, "empty.bin").Length == 0, "zero-byte native attachments remain downloadable");
        var repeated = input with { FileName = "compressible.txt", Bytes = Encoding.UTF8.GetBytes(new string('a', 100000)) };
        var compressed = PdfAttachmentEditor.Add(original, repeated);
        check(File(compressed, repeated.FileName).EncodedBytes < 100000 && Contents(compressed, repeated.FileName).SequenceEqual(repeated.Bytes), "Flate authoring compresses eligible bytes losslessly");
        var batch = PdfAttachmentEditor.AddRange(original, [input, repeated]);
        check(PdfEmbeddedFiles.Read(batch.Sources[0].Bytes).Count == 2, "atomic add batch embeds every chosen file");
        SharedReferences(added, input, check);
        var capacity = PdfDocumentEngine.Open(EmbeddedFileTests.Fixture(512), "full.pdf");
        reject(() => PdfAttachmentEditor.Add(capacity, input), "authoring retains the 512-entry catalog bound");
        Directory.CreateDirectory("artifacts/engine"); Directory.CreateDirectory("artifacts/structured");
        foreach (var (name, document) in new[] { ("original", original), ("added", added), ("replaced", replaced), ("removed", removed), ("batch", batch) })
            System.IO.File.WriteAllBytes("artifacts/structured/attachment-edit-" + name + ".pdf", document.Sources[0].Bytes);
        System.IO.File.WriteAllBytes("artifacts/engine/attachment-edit-original.pdf", original.Sources[0].Bytes);
        System.IO.File.WriteAllBytes("artifacts/engine/attachment-edit-input.txt", input.Bytes);
        System.IO.File.WriteAllBytes("artifacts/engine/attachment-edit-replacement.txt", replacement);
        Benchmark(original, input);
    }
    private static PdfWorkspace ChangeSource(PdfWorkspace source, Action<PdfDocument> action)
    {
        using var native = PdfDocumentEngine.OpenNative(source.Sources[0].Bytes); action(native);
        return source with { Sources = [source.Sources[0] with { Bytes = PdfDocumentEngine.Bytes(native) }] };
    }
    private static void SharedReferences(PdfWorkspace added, PdfAttachmentInput input, Action<bool, string> check)
    {
        var shared = ChangeSource(added, n =>
        {
            var names = PdfObjects.Dictionary(n.Internals.Catalog.Elements["/Names"])!;
            var tree = PdfObjects.Dictionary(names.Elements["/EmbeddedFiles"])!;
            var pairs = PdfObjects.Array(tree.Elements["/Names"])!;
            var spec = pairs.Elements[1];
            pairs.Elements.Add(new PdfString("Ω-alias", PdfStringEncoding.Unicode)); pairs.Elements.Add(spec);
            n.Internals.Catalog.Elements["/AF"] = new PdfArray(n, spec);
            names.Elements["/Dests"] = new PdfDictionary(n);
        });
        var entries = PdfEmbeddedFiles.Read(shared.Sources[0].Bytes);
        var selected = entries.Single(e => e.Key != "Ω-alias");
        var renamed = PdfAttachmentEditor.SetDescription(shared, selected, "Isolated catalog description");
        check(PdfEmbeddedFiles.Read(renamed.Sources[0].Bytes).Single(e => e.Key == "Ω-alias").Description == input.Description, "shared catalog specifications are copy-on-write for description edits");
        using (var native = PdfDocumentEngine.OpenNative(renamed.Sources[0].Bytes))
        {
            var af = PdfObjects.Dictionary(PdfObjects.Array(native.Internals.Catalog.Elements["/AF"])!.Elements[0])!;
            check(af.Elements.GetString("/Desc") == input.Description, "associated-file references are not silently retargeted");
            check(PdfObjects.Dictionary(native.Internals.Catalog.Elements["/Names"])!.Elements.ContainsKey("/Dests"), "unrelated catalog name trees survive attachment changes");
        }
        var removed = PdfAttachmentEditor.Remove(renamed, PdfEmbeddedFiles.Read(renamed.Sources[0].Bytes).Single(e => e.Key == selected.Key));
        var alias = PdfEmbeddedFiles.Read(removed.Sources[0].Bytes).Single();
        check(alias.Key == "Ω-alias" && PdfEmbeddedFiles.Extract(removed.Sources[0].Bytes, alias).SequenceEqual(input.Bytes), "removing one catalog key does not erase shared native files");
    }
    private static void Benchmark(PdfWorkspace original, PdfAttachmentInput input)
    {
        var inputs = Enumerable.Range(0, 8).Select(i => input with { FileName = $"batch-{i}.txt" }).ToArray();
        PdfWorkspace Apply(bool batch)
        {
            if (batch) return PdfAttachmentEditor.AddRange(original, inputs);
            var current = original; foreach (var file in inputs) current = PdfAttachmentEditor.Add(current, file); return current;
        }
        for (var i = 0; i < 3; i++) { Apply(false); Apply(true); }
        var results = new List<object>();
        for (var sample = 0; sample < 7; sample++)
            foreach (var batch in sample % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                var begin = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.GetTimestamp();
                var result = Apply(batch); var elapsed = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
                var allocation = GC.GetAllocatedBytesForCurrentThread() - begin;
                if (PdfEmbeddedFiles.Read(result.Sources[0].Bytes).Count != 8) throw new InvalidDataException("Benchmark output mismatch.");
                results.Add(new { sample, batch, milliseconds = elapsed, managedBytes = allocation, outputBytes = result.Sources[0].Bytes.Length });
            }
        System.IO.File.WriteAllText("artifacts/engine/attachment-authoring-performance.json", JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), samples = results,
            scope = "Eight tiny files; three warmups, seven alternating-order samples. Batch versus eight serial Add calls. Includes parse, graph guards, compression, native writing; excludes rendering, UI, input IO and native/GPU allocations. Verification outside timing. Synthetic, not a global speedup." }));
    }
}
