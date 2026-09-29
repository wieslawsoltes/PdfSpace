using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Layout;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class ObjectSnapEditingTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var initial = new RectD(10, 20, 100, 50);
        var index = new ObjectSnapIndex([new(151, 92, 0, 0)]);
        var snapped = index.SnapResize(initial, 4, new(40, 20), 3);
        check(snapped.Bounds == new RectD(10, 20, 141, 72), "corner resize snaps grabbed edges without moving fixed corner");
        check(snapped.Correction == new PointD(1, 2), "resize correction measures grabbed-edge correction, not translation");
        check(snapped.VerticalGuide == new ObjectSnapGuide(151, 20, 92) && snapped.HorizontalGuide == new ObjectSnapGuide(92, 10, 151), "resize guides span actual snapped bounds");
        check(index.SnapResize(initial, 3, new(40, 50), 3).Bounds == new RectD(10, 20, 141, 50), "side resize never snaps inactive edge");
        check(index.SnapResize(initial, 4, new(40, 20), .5).Bounds == new RectD(10, 20, 140, 70), "resize outside snap tolerance keeps raw geometry");
        check(new ObjectSnapIndex([new(10, 20, 0, 0)]).SnapResize(initial, 3, new(20, 0), 4).VerticalGuide is null, "resize does not snap its stationary edge");
        check(index.SnapResize(initial, 4, default, 100).Bounds == initial && index.SnapResize(initial, 4, default, 100).VerticalGuide is null, "stationary resize cannot snap or create an undo entry");
        var centered = new ObjectSnapIndex([new(131, 81, 0, 0)]).SnapResize(initial, 4, new(20, 10), 3, fromCenter: true);
        check(centered.Bounds == new RectD(-11, 9, 142, 72) && centered.Bounds.Center == initial.Center, "center-based edge snapping preserves original pivot");
        var aspect = new ObjectSnapIndex([new(152, 100, 0, 0)]).SnapResize(initial, 4, new(40, 19), 3, preserveAspect: true);
        check(aspect.Bounds == new RectD(10, 20, 142, 71), "aspect-coupled snapping solves both extents from one aligned edge");
        check(aspect.VerticalGuide is not null && aspect.HorizontalGuide is null, "aspect snap does not display an unsatisfied second guide");
        var conflict = new ObjectSnapIndex([new(152, 91.5, 0, 0)]).SnapResize(initial, 4, new(40, 19), 4, preserveAspect: true);
        check(conflict.Bounds == aspect.Bounds && conflict.HorizontalGuide is null, "conflicting aspect anchors choose smallest maximum edge displacement");
        var exact = new ObjectSnapIndex([new(152, 91, 0, 0)]).SnapResize(initial, 4, new(40, 19), 3, preserveAspect: true);
        check(exact.VerticalGuide is not null && exact.HorizontalGuide is not null, "compatible aspect anchors retain both guides");
        var minimum = new ObjectSnapIndex([new(9, 0, 0, 0)]).SnapResize(initial, 3, new(-99, 0), 3);
        check(minimum.Bounds.Width == 1 && minimum.VerticalGuide is null, "snapping cannot cross the fixed anchor");
        var steep = new RectD(0, 0, 10, 1000);
        check(new ObjectSnapIndex([new(12, 0, 0, 0)]).SnapResize(steep, 4, new(1, 100), 2, true).VerticalGuide is null,
            "aspect snapping respects tolerance on amplified perpendicular edge");
        var sideAspect = index.SnapResize(initial, 3, new(40, 0), 3, true);
        check(sideAspect.Bounds == new RectD(10, 9.75, 141, 70.5), "proportional side resize keeps perpendicular center fixed");
        reject(() => index.SnapResize(initial, 8, new(1, 2), 2), "resize snapping rejects unknown handles");
        reject(() => index.SnapResize(initial, 3, new(double.NaN, 0), 2), "resize snapping rejects invalid displacement");
        reject(() => index.SnapResize(initial, 3, new(1, 2), double.PositiveInfinity), "resize snapping rejects infinite tolerance");
        reject(() => index.SnapResize(initial, 3, new(1, 2), 2, minimumExtent: 0), "resize snapping rejects invalid minimum extent");
        foreach (var handle in Enumerable.Range(0, 8))
        {
            var raw = SelectionTransform.Resize(initial, handle, new(-17, 19), true, true);
            var found = new ObjectSnapIndex([raw]).SnapResize(initial, handle, new(-17, 19), 0, true, true);
            check(found.Bounds.Center.Distance(initial.Center) < 1e-10 && Math.Abs(found.Bounds.Width / found.Bounds.Height - 2) < 1e-10,
                $"centered aspect resize retains constraints for handle {handle}");
        }
        var points = new ObjectSnapIndex([new(100, 200, 40, 60)]);
        var point = points.SnapPoint(new(119, 228), 3);
        check(point.Position == new PointD(120, 230) && point.Correction == new PointD(1, 2), "point snap uses target center with one coordinate query per axis");
        check(points.SnapPoint(new(119, 228), 3, vertical: false).Position == new PointD(120, 228), "point snapping retains horizontal axis lock");
        check(points.SnapPoint(new(119, 228), 3, horizontal: false).Position == new PointD(119, 230), "point snapping retains vertical axis lock");
        check(points.SnapPoint(new(120, 230), 0).VerticalGuide is not null, "exact point alignment has a guide at zero tolerance");
        check(points.SnapPoint(new(119, 228), 3, false, false).Position == new PointD(119, 228), "disabled point axes preserve original location");
        reject(() => points.SnapPoint(new(double.NaN, 0), 1), "point snapping rejects nonfinite coordinates");
        reject(() => points.SnapPoint(default, -1), "point snapping rejects negative tolerance");
        var duplicates = new ObjectSnapIndex(Enumerable.Repeat(new RectD(100, 200, 40, 60), 20000).ToArray());
        check(duplicates.Count == 20000 && duplicates.HorizontalAnchorCount == 3 && duplicates.VerticalAnchorCount == 3,
            "20000 identical targets retain only six unique guide coordinates");
        var priority = new ObjectSnapIndex([new(100, 500, 0, 0), new(100, 200, 0, 0)]).SnapPoint(new(101, 300), 2, vertical: false);
        check(priority.VerticalGuide == new ObjectSnapGuide(100, 300, 500), "compacted duplicate coordinate preserves first-target guide priority");
        Randomized(check);
        NativeRoundtrip(check);
        Measure(check);
    }

    private static void Randomized(Action<bool, string> check)
    {
        var random = new Random(540);
        var targets = Enumerable.Range(0, 180).Select(_ => new RectD(random.Next(-250, 600), random.Next(-250, 600), random.Next(0, 80), random.Next(0, 90))).ToArray();
        var index = new ObjectSnapIndex(targets);
        var pointEqual = true; var constraints = true; var guideValid = true;
        for (var i = 0; i < 2500; i++)
        {
            var point = new PointD(random.Next(-300, 800) + .2, random.Next(-300, 800) + .3);
            var tolerance = i % 8;
            var expected = new PointD(BruteAxis(targets, point.X, tolerance, true), BruteAxis(targets, point.Y, tolerance, false));
            pointEqual &= index.SnapPoint(point, tolerance).Position.Distance(expected) < 1e-10;
            var initial = new RectD(point.X, point.Y, random.Next(30, 130), random.Next(30, 130));
            var delta = new PointD(random.Next(-10, 10) + .2, random.Next(-10, 10) + .3);
            var handle = i % 8; var center = (i & 8) != 0; var aspect = (i & 16) != 0;
            var raw = SelectionTransform.Resize(initial, handle, delta, aspect, center);
            var result = index.SnapResize(initial, handle, delta, tolerance, aspect, center);
            var b = result.Bounds;
            constraints &= b.IsFinite && b.Width > 0 && b.Height > 0;
            if (center) constraints &= b.Center.Distance(initial.Center) < 1e-8;
            if (aspect) constraints &= Math.Abs(b.Width / b.Height - initial.Width / initial.Height) < 1e-9;
            var hx = handle is 0 or 6 or 7 ? -1 : handle is 2 or 3 or 4 ? 1 : 0;
            var hy = handle is 0 or 1 or 2 ? -1 : handle is 4 or 5 or 6 ? 1 : 0;
            if (!center && hx != 0) constraints &= Math.Abs((hx < 0 ? b.Right : b.X) - (hx < 0 ? initial.Right : initial.X)) < 1e-8;
            if (!center && hy != 0) constraints &= Math.Abs((hy < 0 ? b.Bottom : b.Y) - (hy < 0 ? initial.Bottom : initial.Y)) < 1e-8;
            if (result.VerticalGuide is { } v) guideValid &= hx != 0 && Math.Abs(v.Coordinate - (hx < 0 ? b.X : b.Right)) < 1e-7 && Math.Abs(result.Correction.X) <= tolerance + 1e-8;
            if (result.HorizontalGuide is { } h) guideValid &= hy != 0 && Math.Abs(h.Coordinate - (hy < 0 ? b.Y : b.Bottom)) < 1e-7 && Math.Abs(result.Correction.Y) <= tolerance + 1e-8;
            if (result.VerticalGuide is null && result.HorizontalGuide is null) constraints &= b == raw;
        }
        check(pointEqual, "2500 point snaps match independent exhaustive coordinate search");
        check(constraints, "2500 random resizes preserve pivot, anchors, aspect and no-snap identity");
        check(guideValid, "2500 constrained resize guides agree with actual grabbed edges and tolerance");
    }

    private static double BruteAxis(RectD[] targets, double coordinate, double tolerance, bool x)
    {
        var best = double.PositiveInfinity; var found = coordinate;
        foreach (var b in targets)
            foreach (var a in new[] { 0d, .5, 1d })
            {
                var target = (x ? b.X : b.Y) + a * (x ? b.Width : b.Height);
                var distance = Math.Abs(target - coordinate);
                if (distance <= tolerance && distance < best) { best = distance; found = target; }
            }
        return found;
    }

    internal static PdfWorkspace Fixture()
    {
        var document = new PdfWorkspace { Title = "Snap editing.pdf", Pages = [new() { Width = 600, Height = 700 }] };
        document = PdfObjectEditor.InsertPath(document, 0, [new("re", [50, 100, 100, 60])]);
        document = PdfObjectEditor.InsertPath(document, 0, [new("re", [300, 250, 80, 90])]);
        return PdfObjectEditor.InsertPath(document, 0, [new("m", [100, 450]), new("c", [130, 420, 200, 510, 220, 450]), new("l", [220, 520]), new("h", [])]);
    }

    private static void NativeRoundtrip(Action<bool, string> check)
    {
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        var original = Fixture(); var objects = PdfObjectEditor.Read(original, 0);
        var index = new ObjectSnapIndex([objects[1].Bounds]);
        var bounds = index.SnapResize(objects[0].Bounds, 3, new(149, 0), 6).Bounds;
        var editor = new EditorSession(original);
        editor.Execute("Snapped resize", d => PdfObjectEditor.SetBounds(d, [objects[0]], bounds));
        var bytes = PdfDocumentEngine.Save(editor.Document, font).Bytes;
        var reopened = PdfObjectEditor.Read(PdfDocumentEngine.Open(bytes, "resized.pdf"), 0);
        check(reopened[0].Bounds == new RectD(50, 100, 250, 60), "snapped native resize survives serialization with fixed left edge");
        check(reopened[1].Bounds == objects[1].Bounds && reopened[2].Bounds == objects[2].Bounds, "native snapped resize leaves sibling objects unchanged");
        check(editor.UndoCount == 1, "snapped resize uses one native edit transaction");
        editor.Undo(); check(ReferenceEquals(editor.Document, original), "snapped resize undo restores exact source identity");
        Directory.CreateDirectory("artifacts/engine"); Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/engine/snap-editing.pdf", PdfDocumentEngine.Save(original, font).Bytes);
        File.WriteAllBytes("artifacts/structured/snap-resized.pdf", bytes);
    }

    private static void Measure(Action<bool, string> check)
    {
        var targets = Enumerable.Range(0, 20000).Select(i => new RectD(i % 200 * 17, i / 200 * 19, 10, 10)).ToArray();
        var index = new ObjectSnapIndex(targets);
        double Run()
        {
            var sum = 0d;
            for (var i = 0; i < 10000; i++)
            {
                sum += index.SnapPoint(new(i % 3330 + .3, i % 1900 + .2), 6).Position.X;
                sum += index.SnapResize(new(10, 20, 100, 60), i % 8, new(i % 30 + .3, i % 20 + .2), 6, (i & 8) != 0).Bounds.Width;
            }
            return sum;
        }
        Run(); var timer = Stopwatch.StartNew(); var before = GC.GetAllocatedBytesForCurrentThread();
        var checksum = Run(); var allocated = GC.GetAllocatedBytesForCurrentThread() - before; timer.Stop();
        check(allocated == 0 && double.IsFinite(checksum), "10000 point plus 10000 resize queries allocate no managed memory after warmup");
        check(index.HorizontalAnchorCount == 600 && index.VerticalAnchorCount == 300, "dense 20000-object anchor storage compacts from 120000 to 900 entries");
        File.WriteAllText("artifacts/structured/snap-editing-performance.json", JsonSerializer.Serialize(new
        {
            targets = targets.Length, pointQueries = 10000, resizeQueries = 10000, allocatedBytes = allocated,
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, retainedAnchors = index.HorizontalAnchorCount + index.VerticalAnchorCount,
            uncompactedAnchors = targets.Length * 6, checksum, runtime = Environment.Version.ToString(),
            scope = "Warm geometry queries only; construction, PDF rewriting, UI and native/GPU allocations excluded."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
