using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Layout;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class ObjectManipulationTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var index = new ObjectSnapIndex([new(100, 300, 80, 40)]);
        var moving = new RectD(98, 298, 10, 10);
        var snapped = index.Snap(moving, 3);
        check(snapped.Correction == new PointD(2, 2) && snapped.Bounds == new RectD(100, 300, 10, 10), "snap aligns nearest native object edges");
        check(snapped.VerticalGuide == new ObjectSnapGuide(100, 300, 340) && snapped.HorizontalGuide == new ObjectSnapGuide(300, 100, 180), "snap guides span the selection and aligned target");
        check(index.Snap(moving, 1).Bounds == moving, "outside-tolerance snap keeps exact geometry");
        check(index.Snap(moving, 3, vertical: false).Correction == new PointD(2, 0), "horizontal movement lock does not snap the vertical coordinate");
        check(index.Snap(moving, 3, horizontal: false).Correction == new PointD(0, 2), "vertical movement lock does not snap the horizontal coordinate");
        check(index.Snap(moving, 3, false, false).VerticalGuide is null && index.Snap(moving, 3, false, false).Bounds == moving, "disabled snap axes produce no correction or guides");
        check(index.Snap(new(139, 310, 0, 0), 2).Correction.X == 1, "zero-area object can snap to target center");
        check(index.Snap(new(180, 340, 2, 2), 0).VerticalGuide is not null, "zero tolerance still shows an exact edge alignment");
        check(new ObjectSnapIndex([]).Snap(moving, 100).Bounds == moving, "empty snap index preserves position");
        var boxes = new[] { new RectD(100, 200, 0, 10), new RectD(100, 400, 0, 10) };
        var duplicates = new ObjectSnapIndex(boxes);
        boxes[0] = new(999, 999, 0, 0);
        check(duplicates.Snap(new(103, 100, 0, 0), 4).VerticalGuide == new ObjectSnapGuide(100, 100, 210), "nearest lower duplicate uses first target and constructor owns a geometry copy");
        check(duplicates.Snap(new(97, 100, 0, 0), 4).VerticalGuide == new ObjectSnapGuide(100, 100, 210), "nearest upper duplicate uses deterministic target order");
        reject(() => new ObjectSnapIndex([new(0, 0, -1, 2)]), "snap index rejects negative bounds");
        reject(() => new ObjectSnapIndex([new(double.MaxValue, 0, double.MaxValue, 2)]), "snap index rejects overflowing endpoints");
        reject(() => index.Snap(moving, double.NaN), "snap query rejects nonfinite tolerance");
        reject(() => index.Snap(moving, -1), "snap query rejects negative tolerance");
        reject(() => new ObjectSnapIndex(new RectD[20002]), "snap source count is bounded");
        var large = new ObjectSnapIndex([new(0, 0, 1e308, 1)]).Snap(new(0, 0, 1e308, 1), 0);
        check(large.VerticalGuide is { } lg && double.IsFinite(lg.Coordinate), "snap anchor arithmetic avoids intermediate overflow");
        var rng = new Random(530);
        var targets = Enumerable.Range(0, 320).Select(_ => new RectD(rng.Next(-1000, 1000), rng.Next(-1000, 1000), rng.Next(0, 120), rng.Next(0, 100))).ToArray();
        var search = new ObjectSnapIndex(targets);
        var equivalent = true;
        for (var i = 0; i < 2000; i++)
        {
            var rect = new RectD(rng.Next(-1200, 1200) + .125, rng.Next(-1200, 1200) + .25, rng.Next(0, 150), rng.Next(0, 150));
            var tolerance = i % 17;
            var got = search.Snap(rect, tolerance).Correction; var expected = Brute(targets, rect, tolerance);
            if (got != expected) { Console.WriteLine($"Mismatch {i}: {rect}, tolerance={tolerance}, got={got}, expected={expected}"); equivalent = false; break; }
        }
        check(equivalent, "2000 snapping queries match an exhaustive independent anchor search");

        var center = new PointD(10, 20); var start = new PointD(10, -30);
        check(Math.Abs(SelectionRotation.DeltaDegrees(center, start, new(60, 20)) - 90) < 1e-10, "pointer rotation is clockwise in logical page coordinates");
        check(Math.Abs(SelectionRotation.DeltaDegrees(center, start, new(-40, 20)) + 90) < 1e-10, "pointer rotation supports counterclockwise motion");
        check(SelectionRotation.DeltaDegrees(center, start, start) == 0, "stationary rotation handle is exact identity");
        check(SelectionRotation.DeltaDegrees(center, center, start) == 0 && SelectionRotation.DeltaDegrees(center, start, center) == 0, "rotation through its pivot avoids undefined angles");
        var point = PdfAffineMatrix.Around(center, PdfAffineMatrix.Rotate(37)).Transform(start);
        check(SelectionRotation.DeltaDegrees(center, start, point, 15) == 30, "Shift rotation snaps to fifteen-degree steps");
        var beforeWrap = new PointD(Math.Cos(179 * Math.PI / 180), Math.Sin(179 * Math.PI / 180));
        var afterWrap = new PointD(Math.Cos(-179 * Math.PI / 180), Math.Sin(-179 * Math.PI / 180));
        check(Math.Abs(SelectionRotation.DeltaDegrees(default, beforeWrap, afterWrap) - 2) < 1e-10, "rotation is continuous across the angle branch cut");
        reject(() => SelectionRotation.DeltaDegrees(center, start, new(double.NaN, 0)), "rotation rejects nonfinite pointer values");
        reject(() => SelectionRotation.DeltaDegrees(center, start, point, -1), "rotation rejects invalid snapping step");
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var page = new PdfPageState { Width = 600, Height = 800, Rotation = rotation, Crop = new(20, 40, 540, 720) };
            var c = new PointD(200, 300); var a = new PointD(200, 200); var b = new PointD(300, 300);
            var placement = new PagePlacement(0, new(100, 70, page.DisplayWidth * 1.75, page.DisplayHeight * 1.75));
            var roundA = placement.ToPage(page, placement.ToScreen(page, a, 1.75), 1.75);
            var roundB = placement.ToPage(page, placement.ToScreen(page, b, 1.75), 1.75);
            check(Math.Abs(SelectionRotation.DeltaDegrees(c, roundA, roundB) - 90) < 1e-9, $"rotation pointer mapping respects crop and page rotation {rotation}");
        }
        var original = ObjectEditingSample.Create();
        var objects = PdfObjectEditor.Read(original, 0);
        var path = objects.First(o => o.Kind == PdfPageObjectKind.Path);
        var editor = new EditorSession(original);
        editor.Execute("Rotate native object", d => PdfObjectEditor.Transform(d, [path], PdfAffineMatrix.Around(path.Bounds.Center, PdfAffineMatrix.Rotate(45))));
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        var bytes = PdfDocumentEngine.Save(editor.Document, font).Bytes;
        var read = PdfDocumentEngine.Open(bytes, "rotated.pdf");
        var after = PdfObjectEditor.Read(read, 0).First(o => o.Kind == PdfPageObjectKind.Path).Bounds;
        var extent = (path.Bounds.Width + path.Bounds.Height) / Math.Sqrt(2);
        check(after.Center.Distance(path.Bounds.Center) < .02 && Math.Abs(after.Width - extent) < .02 && Math.Abs(after.Height - extent) < .02, "free rotation survives native PDF save/reopen around the original center");
        check(editor.UndoCount == 1, "native rotation is one history transaction");
        editor.Undo(); check(ReferenceEquals(editor.Document, original), "rotation undo restores exact original snapshot");
        editor.Redo(); check(editor.UndoCount == 1, "rotation redo restores edited snapshot");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllBytes("artifacts/structured/manipulation-original.pdf", PdfDocumentEngine.Save(original, font).Bytes);
        File.WriteAllBytes("artifacts/structured/manipulation-rotated.pdf", bytes);
        Measure(check);
    }

    private static PointD Brute(RectD[] targets, RectD moving, double tolerance)
    {
        double Axis(bool x)
        {
            var best = double.PositiveInfinity; var delta = 0d; var targetIndex = int.MaxValue; var sourceAnchor = int.MaxValue;
            for (var t = 0; t < targets.Length; t++)
                for (var a = 0; a < 3; a++)
                    for (var b = 0; b < 3; b++)
                    {
                        var source = (x ? moving.X : moving.Y) + (x ? moving.Width : moving.Height) * (a / 2d);
                        var target = (x ? targets[t].X : targets[t].Y) + (x ? targets[t].Width : targets[t].Height) * (b / 2d);
                        var d = target - source; var distance = Math.Abs(d);
                        if (distance > tolerance) continue;
                        if (distance < best || distance == best && (t < targetIndex || t == targetIndex && a < sourceAnchor))
                        { best = distance; delta = d; targetIndex = t; sourceAnchor = a; }
                    }
            return delta;
        }
        return new(Axis(true), Axis(false));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static double Queries(ObjectSnapIndex index, int count)
    {
        var sum = 0d;
        for (var i = 0; i < count; i++) sum += index.Snap(new(i % 3330 + .3, i % 1900 + .2, 11, 12), 6).Bounds.X;
        return sum;
    }

    private static void Measure(Action<bool, string> check)
    {
        var targets = Enumerable.Range(0, 20000).Select(i => new RectD((i % 200) * 17, (i / 200) * 19, 10, 10)).ToArray();
        var index = new ObjectSnapIndex(targets);
        const int iterations = 10000;
        var checksum = Queries(index, iterations);
        var timer = Stopwatch.StartNew(); var before = GC.GetAllocatedBytesForCurrentThread();
        checksum += Queries(index, iterations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before; timer.Stop();
        check(allocated == 0 && double.IsFinite(checksum), "10000 snap queries against 20000 targets allocate no managed bytes");
        File.WriteAllText("artifacts/structured/object-snap-performance.json", JsonSerializer.Serialize(new
        {
            targets = targets.Length, iterations, allocatedBytes = allocated, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
            checksum, runtime = Environment.Version.ToString(), scope = "Warm snap query only; excludes index construction, PDF rewrite, Skia, and UI."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
