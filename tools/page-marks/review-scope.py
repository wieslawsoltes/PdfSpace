from pathlib import Path
p=Path('src/PdfSpace.Pdf/PdfPageMarks.cs')
s=p.read_text(); start=s.index('    public static IReadOnlyList<PdfPageMarkInfo> Read(PdfWorkspace workspace)'); end=s.index('    public static PdfPageMarkResult Apply',start)
s=s[:start]+'''    public static IReadOnlyList<PdfPageMarkInfo> Read(PdfWorkspace workspace)
    {
        WorkspaceJson.Validate(workspace);
        return ReadCore(workspace, Enumerable.Range(0, workspace.Pages.Length), CancellationToken.None);
    }

    /// <summary>Read only the requested pages, opening each referenced native source once.
    /// Unselected pages and unreferenced source buffers are not parsed or certified.</summary>
    public static IReadOnlyList<PdfPageMarkInfo> Read(PdfWorkspace workspace, IReadOnlyList<int> pageIndices,
        CancellationToken cancellationToken = default)
    {
        var indices = ValidatePages(workspace, pageIndices);
        return ReadCore(workspace, indices, cancellationToken);
    }

    private static IReadOnlyList<PdfPageMarkInfo> ReadCore(PdfWorkspace workspace, IEnumerable<int> indices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<PdfPageMarkInfo>();
        var fingerprint = new PdfMarkFingerprint();
        var sources = workspace.Sources.ToDictionary(source => source.Id);
        var groups = indices.Select(index => (Page: workspace.Pages[index], Index: index))
            .Where(item => item.Page.SourceId.HasValue).GroupBy(item => item.Page.SourceId!.Value);
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = PdfDocumentEngine.OpenNative(sources[group.Key].Bytes);
            foreach (var (page, index) in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (page.SourcePage > document.PageCount)
                    throw new InvalidDataException("Page-mark source page does not exist.");
                foreach (var owned in ReadOwned(document.Pages[page.SourcePage - 1], index, fingerprint))
                    result.Add(owned.Info);
            }
        }
        result.Sort((left, right) => left.PageIndex != right.PageIndex
            ? left.PageIndex.CompareTo(right.PageIndex) : left.Settings.Kind.CompareTo(right.Settings.Kind));
        return result;
    }

'''+s[end:]
s=s.replace('        var selected = indices.ToHashSet();\n        var existing = Read(workspace).Where(m => selected.Contains(m.PageIndex) && m.Settings.Kind == settings.Kind).ToDictionary(m => m.PageIndex);','        var existing = ReadCore(workspace, indices, cancellationToken).Where(m => m.Settings.Kind == settings.Kind).ToDictionary(m => m.PageIndex);')
s=s.replace('        var selected = indices.ToHashSet();\n        var marks = Read(workspace).Where(m => selected.Contains(m.PageIndex) && m.Settings.Kind == kind).ToArray();','        var marks = ReadCore(workspace, indices, cancellationToken).Where(m => m.Settings.Kind == kind).ToArray();')
s=s.replace('        _ = page.Contents; // Normalize PDFsharp content wrappers before comparing resolved stream identities.\n','')
s=s.replace('        if (list.Elements.Count > 2) throw new InvalidDataException("Too many page-mark ownership records.");','        if (list.Elements.Count > 2) throw new InvalidDataException("Too many page-mark ownership records.");\n        _ = page.Contents; // Resolve wrappers only on pages that actually have ownership metadata.')
p.write_text(s)
p=Path('src/PdfSpace.Workbench/PdfWorkbench.PageMarks.cs');s=p.read_text().replace('PdfPageMarks.Read(Session.Document).FirstOrDefault','PdfPageMarks.Read(Session.Document, [Session.CurrentPage]).FirstOrDefault');p.write_text(s)
p=Path('tests/PdfSpace.Tests/PageMarkTests.cs');s=p.read_text();needle='        check(marks.Count == 2 && marks.All(m => m.Intact), "native page-mark ownership survives serialization");';assert needle in s;s=s.replace(needle,needle+'\n        PageMarkReadScopeTests.Run(apply.Workspace, settings, font, check, reject);');p.write_text(s)
p=Path('tests/PdfSpace.Tests/PageMarkReadScopeTests.cs');p.write_text('''using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class PageMarkReadScopeTests
{
    public static void Run(PdfWorkspace marked, PdfPageMarkSettings settings, SKTypeface font,
        Action<bool, string> check, Action<Action, string> reject)
    {
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
''')
p=Path('docs/page-marks.md');s=p.read_text();s+='''\n## Range-scoped inspection\n\n`PdfPageMarks.Read(workspace, pageIndices, cancellationToken)` inspects an ascending distinct range of at most 500 pages and opens each referenced source once. The inspector uses the current page, and Apply/Remove inspect only their requested range before materialization. Unreferenced source buffers are not parsed and pages without ownership metadata do not normalize their content arrays. Full-document `Read(workspace)` still inspects every referenced page. This is not certification of unselected content; all structured-save checks remain in force when an effective edit materializes the document.\n''';p.write_text(s)
