using System.Diagnostics;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;
using PdfSpace.Skia;
using SkiaSharp;

internal static class PageLabelTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        foreach (var (style, number, expected) in new[]
        {
            (PdfPageLabelStyle.Decimal, 42, "42"), (PdfPageLabelStyle.RomanLower, 49, "xlix"),
            (PdfPageLabelStyle.RomanUpper, 1994, "MCMXCIV"), (PdfPageLabelStyle.LettersUpper, 26, "Z"),
            (PdfPageLabelStyle.LettersUpper, 27, "AA"), (PdfPageLabelStyle.LettersUpper, 28, "BB"),
            (PdfPageLabelStyle.LettersLower, 53, "aaa"), (PdfPageLabelStyle.PrefixOnly, 1, "")
        }) check(new PdfPageLabel { Style = style, Number = number }.Format() == expected, $"PDF label formatting {style}/{number}");
        check(new PdfPageLabel { Prefix = "章-Żółć-", Number = 2 }.Format() == "章-Żółć-2", "Unicode label prefixes are retained exactly");
        foreach (var bad in new[]
        {
            new PdfPageLabel { Number = 0 }, new PdfPageLabel { Style = (PdfPageLabelStyle)100 },
            new PdfPageLabel { Prefix = "bad\nlabel" }, new PdfPageLabel { Prefix = "\ud800" },
            new PdfPageLabel { Prefix = new string('p', 257) }, new PdfPageLabel { Style = PdfPageLabelStyle.LettersUpper, Number = int.MaxValue },
            new PdfPageLabel { Style = PdfPageLabelStyle.RomanUpper, Number = int.MaxValue }
        }) reject(() => bad.Format(), "page label rejects invalid or excessively expanded data");
        var blank = new PdfWorkspace { Pages = Enumerable.Range(0, 6).Select(_ => new PdfPageState()).ToArray() };
        check(ReferenceEquals(PdfPageLabels.Reset(blank, 0, 6), blank), "resetting blank default labels is an exact no-op");
        var roman = new PdfPageLabel { Style = PdfPageLabelStyle.RomanLower };
        var front = PdfPageLabels.Apply(blank, 0, 2, roman);
        var body = PdfPageLabels.Apply(front, 2, 4, new PdfPageLabel { Prefix = "A-" });
        var index = new PageLabelIndex(body.Pages);
        check(Enumerable.Range(0, 6).Select(i => index[i]).SequenceEqual(new[] { "i", "ii", "A-1", "A-2", "A-3", "A-4" }), "label sections preserve unselected front matter");
        check(ReferenceEquals(PdfPageLabels.Apply(body, 2, 4, new PdfPageLabel { Prefix = "A-" }), body), "identical labels preserve workspace identity");
        check(ReferenceEquals(PdfPageLabels.Initialize(body), body), "initialized label inspection never rewrites a workspace");
        var session = new EditorSession(blank); session.Execute("Labels", _ => body);
        check(session.UndoCount == 1, "whole label assignment is one undo transaction");
        session.Undo(); check(ReferenceEquals(session.Document, blank), "label undo restores exact snapshot");
        session.Redo(); check(ReferenceEquals(session.Document, body), "label redo restores exact snapshot");
        check(index.Resolve("A-3", out var resolved) == PageLabelMatch.Found && resolved == 4, "label navigation resolves exact labels");
        check(index.Resolve("#3", out resolved) == PageLabelMatch.Found && resolved == 2, "physical-page navigation is unambiguous");
        check(index.Resolve("II", out _) == PageLabelMatch.NotFound, "label lookup preserves case");
        var duplicate = PdfPageLabels.Apply(blank, 0, 2, new PdfPageLabel { Style = PdfPageLabelStyle.PrefixOnly, Prefix = "Cover" });
        check(new PageLabelIndex(duplicate.Pages).Resolve("Cover", out _) == PageLabelMatch.Ambiguous, "duplicate labels cannot silently retarget navigation");
        var extend = PdfPageLabels.Extend(front, 2, 2);
        check(extend.Pages[2].Label!.Format() == "iii" && extend.Pages[3].Label!.Format() == "iv" && extend.Pages[4].Label is null, "extend resumes preceding section and preserves following pages");
        reject(() => PdfPageLabels.Extend(blank, 0, 1), "first page cannot extend a nonexistent preceding section");
        reject(() => PdfPageLabels.Apply(blank, 0, 2, new PdfPageLabel { Number = int.MaxValue }), "label numbering overflow rejects the whole batch");
        reject(() => PdfPageLabels.Apply(blank, int.MaxValue, 2, roman), "label range arithmetic is overflow safe");
        reject(() => PdfPageLabels.Reset(blank, 0, 0), "empty label edit range rejected");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        reject(() => PdfPageLabels.Apply(body, 0, 1, roman, cancelled.Token), "cancelled labels preserve input before editing");
        check(WorkspaceJson.Load(WorkspaceJson.Save(body)).Pages[2].Label == body.Pages[2].Label, "label style and value survive workspace JSON");
        var reordered = WorkspacePages.Select(body, [5, 0, 2]);
        check(new PageLabelIndex(reordered.Pages)[0] == "A-4" && new PageLabelIndex(reordered.Pages)[1] == "i", "labels follow reordered and extracted page identities");
        session = new EditorSession(body); session.DuplicatePage();
        check(session.Document.Pages.Take(2).All(p => p.Label == roman), "duplicated pages retain their label without automatic renumbering");
        var combined = WorkspaceComposition.Append(body, body);
        check(combined.Pages[6].Label == body.Pages[0].Label, "combining preserves incoming resolved labels");
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        Directory.CreateDirectory("artifacts/structured");
        var unicode = PdfPageLabels.Apply(blank, 0, 6, new PdfPageLabel { Prefix = "章-Żółć-📄-" });
        var unicodeBytes = PdfDocumentEngine.Save(unicode, font).Bytes;
        check(PdfDocumentEngine.Open(unicodeBytes, "unicode-labels.pdf").Pages[5].Label!.Format() == "章-Żółć-📄-6",
            "Unicode BMP and supplementary page-label prefixes survive native export");
        File.WriteAllBytes("artifacts/structured/labels-unicode.pdf", unicodeBytes);
        var literal = PdfPageLabels.Apply(blank, 0, 2, new PdfPageLabel { Style = PdfPageLabelStyle.PrefixOnly, Prefix = "Cover", Number = 12 });
        check(PdfDocumentEngine.Open(PdfDocumentEngine.Save(literal, font).Bytes, "literal-labels.pdf").Pages[0].Label == literal.Pages[0].Label,
            "prefix-only numbering metadata roundtrips without changing settings");
        var adjacentLiterals = PdfPageLabels.Apply(literal, 2, 2, new PdfPageLabel { Style = PdfPageLabelStyle.PrefixOnly, Prefix = "Cover", Number = 7 });
        var adjacentReload = PdfDocumentEngine.Open(PdfDocumentEngine.Save(adjacentLiterals, font).Bytes, "literal-sections.pdf");
        check(adjacentReload.Pages[0].Label!.Number == 12 && adjacentReload.Pages[2].Label!.Number == 7,
            "prefix-only sections preserve distinct unused starting-number settings");
        using (var implicitNative = new PdfDocument())
        {
            for (var i = 0; i < blank.Pages.Length; i++) implicitNative.AddPage();
            var oldTree = new PdfDictionary(implicitNative);
            implicitNative.Internals.Catalog.Elements["/PageLabels"] = oldTree;
            PdfPageLabels.Write(implicitNative, blank, new Dictionary<Guid, PdfDocument>());
            check(!implicitNative.Internals.Catalog.Elements.ContainsKey("/PageLabels"),
                "implicit-only save removes existing labels without initializing native sources");
            var sources = new Dictionary<Guid, PdfDocument>();
            for (var i = 0; i < 3; i++) PdfPageLabels.Write(implicitNative, blank, sources);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) PdfPageLabels.Write(implicitNative, blank, sources);
            check(GC.GetAllocatedBytesForCurrentThread() == allocated,
                "unlabelled native save path allocates no per-page label or tree objects");
        }
        var bytes = PdfDocumentEngine.Save(body, font).Bytes;
        var reopened = PdfDocumentEngine.Open(bytes, "labels.pdf");
        check(new PageLabelIndex(reopened.Pages)[5] == "A-4", "native PageLabels survives save and reopen");
        using (var native = PdfDocumentEngine.OpenNative(bytes))
            check(PdfObjects.Array(PdfObjects.Dictionary(native.Internals.Catalog.Elements["/PageLabels"])!.Elements["/Nums"])!.Elements.Count == 4,
                "six page labels serialize as two maximal number-tree runs");
        var physical = PdfPageLabels.Apply(blank, 0, 6, new PdfPageLabel());
        var physicalReload = PdfDocumentEngine.Open(PdfDocumentEngine.Save(physical, font).Bytes, "explicit-physical-labels.pdf");
        check(physicalReload.Pages.All(p => p.Label is not null), "explicit physical-looking labels survive native roundtrip");
        check(new PageLabelIndex(WorkspacePages.Select(physicalReload, [5, 0]).Pages)[0] == "6",
            "explicit numeric labels retain page identity after reopen and reordering");
        var alteredPages = body.Pages.Select(p => p with { Bookmark = "changed" }).ToArray();
        check(index.Matches(alteredPages), "annotation and bookmark changes can reuse the label index");
        alteredPages[2] = alteredPages[2] with { Label = new PdfPageLabel { Prefix = "Other-" } };
        check(!index.Matches(alteredPages), "changed label definitions invalidate the navigation index");
        check(index[2] == "A-1", "index owns its definition sequence independently of mutable input arrays");
        check(!index.Matches(WorkspacePages.Select(body, [5, 0, 2]).Pages), "changed page order and count invalidate label index");
        var lastInteger = PdfPageLabels.Apply(blank, 5, 1, new PdfPageLabel { Number = int.MaxValue });
        check(PdfDocumentEngine.Open(PdfDocumentEngine.Save(lastInteger, font).Bytes, "last-integer.pdf").Pages[5].Label!.Number == int.MaxValue,
            "last-page maximum starting integer avoids intermediate overflow");
        var reset = PdfPageLabels.Reset(reopened, 0, 6);
        using (var native = PdfDocumentEngine.OpenNative(PdfDocumentEngine.Save(reset, font).Bytes))
            check(!native.Internals.Catalog.Elements.ContainsKey("/PageLabels"), "physical numbering reset removes redundant native tree");
        var lowLevel = PdfSpace.Documents.PdfReader.Open(bytes, "low-level.pdf");
        check(PdfDocumentEngine.Open(PdfDocumentEngine.Save(lowLevel, font).Bytes, "saved.pdf").Pages[0].Label!.Format() == "i", "low-level imports retain existing native labels on save");
        check(PdfDocumentEngine.PrepareWorkspace(lowLevel).Pages[2].Label!.Format() == "A-1", "old workspace initialization imports source labels");
        TreeChecks(check, reject);

        var original = ObjectEditingSample.Create();
        var labelled = PdfPageLabels.Apply(original, 0, 2, roman);
        check(ReferenceEquals(original.Sources, labelled.Sources) && ReferenceEquals(original.Sources[0].Bytes, labelled.Sources[0].Bytes), "label edits retain original PDF buffers by reference");
        using var renderer = new PdfRenderer();
        check(renderer.ExportPng(original, 0, 1).SequenceEqual(renderer.ExportPng(labelled, 0, 1)), "labels do not alter page rendering");
        var sourceBytes = PdfDocumentEngine.Save(original, font).Bytes;
        var labelledBytes = PdfDocumentEngine.Save(labelled, font).Bytes;
        using (var before = PdfDocumentEngine.OpenNative(sourceBytes))
        using (var after = PdfDocumentEngine.OpenNative(labelledBytes))
            check(before.Pages[0].Contents.CreateSingleContent().Stream.Value.SequenceEqual(after.Pages[0].Contents.CreateSingleContent().Stream.Value), "native label export preserves decoded page content streams");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/labels-original.pdf", sourceBytes);
        File.WriteAllBytes("artifacts/structured/labels-native.pdf", labelledBytes);
        File.WriteAllBytes("artifacts/structured/labels-sections.pdf", bytes);
        File.WriteAllBytes("artifacts/structured/labels-reordered.pdf", PdfDocumentEngine.Save(reordered, font).Bytes);
        Benchmark(check);
    }

    private static void TreeChecks(Action<bool, string> check, Action<Action, string> reject)
    {
        using var doc = new PdfDocument(); for (var i = 0; i < 6; i++) doc.AddPage();
        PdfDictionary Leaf(int key, string style, string prefix, int number)
        {
            var rule = new PdfDictionary(doc); rule.Elements.SetName("/S", style); rule.Elements.SetString("/P", prefix); rule.Elements.SetInteger("/St", number);
            var nums = new PdfArray(doc); nums.Elements.Add(new PdfInteger(key)); nums.Elements.Add(rule);
            var leaf = new PdfDictionary(doc); leaf.Elements["/Nums"] = nums;
            // Number-tree limits, unlike geometric arrays, require integers.
            var limits = new PdfArray(doc); limits.Elements.Add(new PdfInteger(key)); limits.Elements.Add(new PdfInteger(key)); leaf.Elements["/Limits"] = limits;
            return leaf;
        }
        var root = new PdfDictionary(doc); var kids = new PdfArray(doc);
        var left = Leaf(0, "/r", "", 1); var right = Leaf(2, "/A", "章-", 27);
        kids.Elements.Add(left); kids.Elements.Add(right); root.Elements["/Kids"] = kids;
        doc.Internals.Catalog.Elements["/PageLabels"] = root;
        check(PdfPageLabels.Read(doc)[3]!.Format() == "章-BB", "nested native number trees resolve repeated alphabetic labels");
        File.WriteAllBytes("artifacts/structured/labels-nested.pdf", PdfDocumentEngine.Bytes(doc));
        var limits = right.Elements["/Limits"]!;
        right.Elements["/Limits"] = PdfObjects.Numbers(doc, 2, 2);
        reject(() => PdfPageLabels.Read(doc), "real-valued number-tree limits rejected"); right.Elements["/Limits"] = limits;
        var nums = PdfObjects.Array(right.Elements["/Nums"])!; var rule = PdfObjects.Dictionary(nums.Elements[1])!;
        rule.Elements.SetName("/S", "/Unknown"); reject(() => PdfPageLabels.Read(doc), "unknown native numbering style rejected"); rule.Elements.SetName("/S", "/A");
        rule.Elements.SetInteger("/St", 0); reject(() => PdfPageLabels.Read(doc), "zero native start is rejected"); rule.Elements.SetInteger("/St", 27);
        nums.Elements[0] = new PdfInteger(0); reject(() => PdfPageLabels.Read(doc), "duplicate global number-tree keys rejected"); nums.Elements[0] = new PdfInteger(2);
        root.Elements["/Nums"] = nums; reject(() => PdfPageLabels.Read(doc), "ambiguous Kids and Nums rejected"); root.Elements.Remove("/Nums");
        kids.Elements.Add(left); reject(() => PdfPageLabels.Read(doc), "shared number-tree child rejected"); kids.Elements.RemoveAt(2);
        doc.Internals.AddObject(root); kids.Elements.Add(root.Reference!); reject(() => PdfPageLabels.Read(doc), "cyclic number-tree child rejected"); kids.Elements.RemoveAt(2);
        nums.Elements.RemoveAt(1); reject(() => PdfPageLabels.Read(doc), "odd native Nums length rejected"); nums.Elements.Add(rule);
        PdfObjects.Array(left.Elements["/Nums"])!.Elements[0] = new PdfInteger(1); left.Elements.Remove("/Limits");
        reject(() => PdfPageLabels.Read(doc), "label tree without initial page-zero rule rejected");
    }

    private static void Benchmark(Action<bool, string> check)
    {
        var pages = Enumerable.Range(0, 4096).Select(i => new PdfPageState { Label = new PdfPageLabel { Prefix = "Chapter-", Number = i + 1 } }).ToArray();
        var index = new PageLabelIndex(pages); var queries = Enumerable.Range(0, 4096).Select(i => index[i]).ToArray();
        for (var i = 0; i < 10000; i++) index.Resolve(queries[i % queries.Length], out _);
        var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp(); var sum = 0;
        for (var i = 0; i < 100000; i++) { index.Resolve(queries[i % queries.Length], out var page); sum += page; }
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds; var allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        check(allocation == 0 && sum > 0, "100000 indexed label lookups allocate zero managed bytes after warmup");
        File.WriteAllText("artifacts/structured/labels-performance.json", JsonSerializer.Serialize(new
        { runtime = Environment.Version.ToString(), pages = pages.Length, queries = 100000, elapsedMilliseconds = elapsed, currentThreadManagedBytes = allocation,
          scope = "Warmed in-memory exact-label resolution. Excludes index build, native PDF parsing/writing, UI/GPU and process-wide memory." }));
    }
}
