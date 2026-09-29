using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Layout;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class ObjectLayoutTests
{
    internal static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        Structural(check, reject);
        var original = ObjectEditingSample.Create();
        var paths = Paths(original);
        var selected = paths.Take(3).ToArray();
        var reference = selected[1];
        var aligned = PdfObjectEditor.AlignToObject(original, selected, 1, PdfObjectAlignment.Left);
        var alignedPaths = Paths(aligned);
        check(alignedPaths.Take(3).All(p => Near(p.Bounds.X, reference.Bounds.X)), "reference alignment uses the chosen object, not the selection envelope");
        check(alignedPaths[1].Bounds == reference.Bounds && alignedPaths[1].LocalToPage == reference.LocalToPage,
            "reference alignment leaves the reference invocation unchanged");
        var identity = PdfObjectEditor.Transform(original, selected, PdfAffineMatrix.Identity);
        check(ReferenceEquals(identity, original), "validated identity transforms preserve the exact workspace and source buffers");
        var session = new EditorSession(aligned);
        session.Execute("Align again", d => PdfObjectEditor.AlignToObject(d, Paths(d).Take(3).ToArray(), 1, PdfObjectAlignment.Left));
        check(session.UndoCount == 0 && !session.IsDirty, "repeating reference alignment adds no history and remains clean");
        reject(() => PdfObjectEditor.Transform(aligned, selected, PdfAffineMatrix.Identity), "identity transforms still reject stale native selections");
        reject(() => PdfObjectEditor.Transform(original, [selected[0] with { Fingerprint = "invalid" }], PdfAffineMatrix.Identity), "identity transforms still validate native fingerprints");
        reject(() => PdfObjectEditor.Transform(original, [selected[0] with { Bounds = selected[0].Bounds with { X = 999 } }], PdfAffineMatrix.Identity), "identity transforms reject forged native bounds");
        reject(() => PdfObjectEditor.Transform(original, [selected[0], selected[0]], PdfAffineMatrix.Identity), "identity transforms cannot bypass duplicate checks");
        reject(() => PdfObjectEditor.AlignToObject(original, selected, 3, PdfObjectAlignment.Left), "layout rejects an out-of-range reference");
        reject(() => PdfObjectEditor.AlignToObject(original, selected, 0, (PdfObjectAlignment)999), "layout rejects unknown alignment modes");
        reject(() => PdfObjectEditor.MatchSize(original, selected, 0, (PdfObjectSizeMatch)999), "layout rejects unknown size modes");
        reject(() => PdfObjectEditor.DistributeSpacing(original, selected[..2], true), "equal gaps require at least three objects");

        var spaced = PdfObjectEditor.DistributeSpacing(original, selected, true);
        var spacedPaths = Paths(spaced);
        var gap1 = spacedPaths[1].Bounds.X - spacedPaths[0].Bounds.Right;
        var gap2 = spacedPaths[2].Bounds.X - spacedPaths[1].Bounds.Right;
        check(Near(gap1, gap2) && Near(gap1, 11), "native spacing equalizes gaps between different sized objects");
        check(spacedPaths[0].Bounds == selected[0].Bounds && spacedPaths[2].Bounds == selected[2].Bounds,
            "equal gaps leave both outer objects fixed");
        check(spacedPaths[1].Bounds.SizeEqualsForTest(selected[1].Bounds), "gap distribution translates without resizing");
        session = new EditorSession(original);
        session.Execute("Space", _ => spaced);
        session.Execute("Space again", d => PdfObjectEditor.DistributeSpacing(d, Paths(d).Take(3).ToArray(), true));
        check(session.UndoCount == 1, "repeating exact equal spacing adds no redundant undo transaction");
        session.Undo(); check(ReferenceEquals(session.Document, original), "native spacing undo restores original snapshot identity");
        session.Redo(); check(ReferenceEquals(session.Document, spaced), "native spacing redo reuses the committed snapshot");
        reject(() => PdfObjectEditor.DistributeSpacing(aligned, alignedPaths.Take(3).ToArray(), true), "equal gaps reject insufficient space instead of introducing overlap");

        foreach (var dimensions in Enum.GetValues<PdfObjectSizeMatch>())
        {
            var sized = PdfObjectEditor.MatchSize(original, selected, 0, dimensions);
            var after = Paths(sized);
            check(after.Take(3).Select((p, i) => p.Bounds.Center.Distance(selected[i].Bounds.Center) < .02).All(v => v),
                $"size matching {dimensions} preserves every center");
            check(dimensions == PdfObjectSizeMatch.Height || after.Take(3).All(p => Near(p.Bounds.Width, selected[0].Bounds.Width)),
                $"size matching {dimensions} retains the requested widths");
            check(dimensions == PdfObjectSizeMatch.Width || after.Take(3).All(p => Near(p.Bounds.Height, selected[0].Bounds.Height)),
                $"size matching {dimensions} retains the requested heights");
            check(after[0].LocalToPage == selected[0].LocalToPage && after[0].Bounds == selected[0].Bounds,
                $"size matching {dimensions} does not rewrite the reference occurrence");
        }
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var cropped = original.UpdatePage(original.Pages[0].Id, p => p with { Crop = new(40, 80, 540, 720), Rotation = rotation });
            foreach (var alignment in Enum.GetValues<PdfObjectAlignment>())
            {
                var moved = PdfObjectEditor.AlignToPage(cropped, [Paths(cropped)[0]], alignment);
                var page = moved.Pages[0];
                var b = PageGeometry.DisplayBounds(page, Paths(moved)[0].Bounds);
                var value = alignment switch { PdfObjectAlignment.Left => b.X, PdfObjectAlignment.Center => b.Center.X - page.DisplayWidth / 2,
                    PdfObjectAlignment.Right => b.Right - page.DisplayWidth, PdfObjectAlignment.Top => b.Y,
                    PdfObjectAlignment.Middle => b.Center.Y - page.DisplayHeight / 2, _ => b.Bottom - page.DisplayHeight };
                check(Near(value, 0), $"page {alignment} alignment honors visible crop at {rotation} degrees");
            }
        }
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        Directory.CreateDirectory("artifacts/structured");
        Save(original, "layout-original.pdf"); Save(spaced, "layout-spacing.pdf"); Save(aligned, "layout-reference.pdf");
        var matched = PdfObjectEditor.MatchSize(original, selected, 0, PdfObjectSizeMatch.Both);
        Save(matched, "layout-size.pdf");
        var reopened = PdfDocumentEngine.Open(PdfDocumentEngine.Save(spaced, font).Bytes, "spaced.pdf");
        check(Near(Paths(reopened)[1].Bounds.X, 189), "equal gaps survive structured PDF save and reopen");
        var beforeSecond = PdfObjectEditor.Read(original, 1).Select(p => (p.Kind, p.Bounds, p.LocalToPage, p.Text)).ToArray();
        var afterSecond = PdfObjectEditor.Read(spaced, 1).Select(p => (p.Kind, p.Bounds, p.LocalToPage, p.Text)).ToArray();
        check(beforeSecond.SequenceEqual(afterSecond), "layout operations leave the other shared-source page unchanged");
        void Save(PdfWorkspace value, string name) => File.WriteAllBytes(Path.Combine("artifacts/structured", name), PdfDocumentEngine.Save(value, font).Bytes);
    }

    private static bool SizeEqualsForTest(this RectD a, RectD b) => Near(a.Width, b.Width) && Near(a.Height, b.Height);
    private static bool Near(double a, double b) => Math.Abs(a - b) < .02;
    private static PdfPageObject[] Paths(PdfWorkspace workspace) => PdfObjectEditor.Read(workspace, 0).Where(p => p.Kind == PdfPageObjectKind.Path).ToArray();

    private static void Structural(Action<bool, string> check, Action<Action, string> reject)
    {
        var basis = new PdfPageObject(Guid.NewGuid(), Guid.NewGuid(), 1, "same-source", "", 0, 0,
            PdfPageObjectKind.Path, new(10, 10, 20, 30), PdfAffineMatrix.Identity, "fingerprint", true, "");
        PdfPageObject Item(string scope, int start, int end) => basis with { ScopePath = scope, Start = start, End = end };
        reject(() => PdfObjectSelectionValidator.Validate([]), "selection preflight rejects empty batches");
        reject(() => PdfObjectSelectionValidator.Validate(new PdfPageObject[1001]), "selection preflight bounds allocation before scanning entries");
        reject(() => PdfObjectSelectionValidator.Validate([null!]), "selection preflight rejects null descriptors");
        reject(() => PdfObjectSelectionValidator.Validate([Item("", 0, 1), Item("", 1, 2)]), "inclusive native ranges reject shared endpoints");
        reject(() => PdfObjectSelectionValidator.Validate([Item("", 1, 1), Item("1/2", 5, 5)]), "selected ancestor cannot overlap a nested descendant");
        reject(() => PdfObjectSelectionValidator.Validate([Item("2", 3, 3), Item("2/3", 5, 5)]), "selected ancestor cannot overlap direct children");
        reject(() => PdfObjectSelectionValidator.Validate([basis, basis with { SourceId = Guid.NewGuid() }]), "selection preflight rejects mixed source identities");
        reject(() => PdfObjectSelectionValidator.Validate([Item("", -1, 0)]), "selection preflight rejects invalid operator indices");
        PdfObjectSelectionValidator.Validate([Item("", 1, 1), Item("10/2", 5, 5)]);
        check(true, "scope ancestry distinguishes complete numeric path segments");
        var random = new Random(550);
        var scopes = new[] { "", "1", "10", "1/2", "1/20", "10/2", "7/3/9" };
        var equivalent = true;
        for (var sample = 0; sample < 600; sample++)
        {
            var items = Enumerable.Range(0, random.Next(1, 30)).Select(_ =>
            {
                var start = random.Next(0, 80);
                return Item(scopes[random.Next(scopes.Length)], start, start + random.Next(0, 5));
            }).ToArray();
            bool accepted;
            try { PdfObjectSelectionValidator.Validate(items); accepted = true; } catch (ArgumentException) { accepted = false; }
            if (accepted != LegacyAccepts(items)) { equivalent = false; break; }
        }
        check(equivalent, "sorted interval and ancestry preflight matches 600 exhaustive legacy selections");
        var dense = Enumerable.Range(0, 1000).Select(i => Item("", i * 3, i * 3 + 1)).Reverse().ToArray();
        PdfObjectSelectionValidator.Validate(dense); _ = LegacyAccepts(dense);
        var timer = new Stopwatch();
        var startBytes = GC.GetAllocatedBytesForCurrentThread(); timer.Start();
        var legacyOk = LegacyAccepts(dense); timer.Stop();
        var legacyBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes; var legacyMs = timer.Elapsed.TotalMilliseconds;
        startBytes = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
        PdfObjectSelectionValidator.Validate(dense); timer.Stop();
        var optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - startBytes; var optimizedMs = timer.Elapsed.TotalMilliseconds;
        check(legacyOk && optimizedBytes < legacyBytes / 8, "1000-object selection preflight removes quadratic temporary-string allocation");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllText("artifacts/structured/object-selection-preflight-performance.json", JsonSerializer.Serialize(new
        {
            objects = dense.Length, legacyBytes, optimizedBytes, legacyMs, optimizedMs, runtime = Environment.Version.ToString(),
            scope = "One warmed structural preflight over 1000 independent occurrences. Excludes native validation, PDF parsing/writing, UI, rendering and GPU memory. Timings are observations, not assertions."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool LegacyAccepts(PdfPageObject[] objects)
    {
        static string Child(PdfPageObject o) => o.ScopePath.Length == 0 ? o.Start.ToString(CultureInfo.InvariantCulture) : o.ScopePath + "/" + o.Start;
        if (objects.Select(o => o.ScopePath + ":" + o.Start + ":" + o.End).Distinct().Count() != objects.Length) return false;
        foreach (var outer in objects)
            foreach (var inner in objects)
                if (!ReferenceEquals(outer, inner) && ((outer.ScopePath == inner.ScopePath && outer.Start <= inner.End && inner.Start <= outer.End) ||
                    inner.ScopePath == Child(outer) || inner.ScopePath.StartsWith(Child(outer) + "/", StringComparison.Ordinal))) return false;
        return true;
    }
}
