using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Layout;

internal static class LayoutIndexTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var random = new Random(91272);
        var pages = Enumerable.Range(0, 4096).Select(i => new PdfPageState { Width = random.Next(100, 1000), Height = random.Next(100, 1400), Rotation = i % 4 * 90 }).ToArray();
        foreach (var mode in Enum.GetValues<PageLayoutMode>())
        {
            var index = new PageLayoutIndex(pages, mode); var ok = true; var visible = true; var hits = true; var nearest = true;
            for (var iteration = 0; iteration < 35; iteration++)
            {
                var zoom = .1 + random.NextDouble() * 7.9; var width = random.Next(320, 2500); var pan = random.Next(-100, 100);
                var current = random.Next(pages.Length); var scroll = random.NextDouble() * index.TotalHeight(zoom, current);
                var expected = PageLayout.Arrange(pages, width, zoom, scroll, pan, mode, current);
                foreach (var placement in expected)
                {
                    var actual = index.Place(placement.Index, width, zoom, scroll, pan);
                    ok &= actual.Bounds.Center.Distance(placement.Bounds.Center) < .0001 && Math.Abs(actual.Bounds.Height-placement.Bounds.Height) < .0001;
                }
                var rect = new RectD(0, 0, width, 960).Inflate(30);
                var expectedVisible = expected.Where(p => p.Bounds.Intersects(rect)).Select(p => p.Index).ToArray();
                var actualVisible = index.Visible(width, 960, zoom, scroll, pan, current).Where(p => p.Bounds.Intersects(rect)).Select(p => p.Index).ToArray();
                visible &= expectedVisible.SequenceEqual(actualVisible);
                var point = new PointD(random.NextDouble() * width, random.NextDouble() * 960);
                hits &= expected.Where(p => p.Bounds.Contains(point)).Select(p => p.Index).DefaultIfEmpty(-1).First() == index.HitTest(point, width, zoom, scroll, pan, current);
                nearest &= expected.OrderBy(p => Math.Abs(p.Bounds.Center.Y - 400)).First().Index == index.NearestPage(scroll + 400, zoom, current);
            }
            check(ok, $"indexed {mode} placements match legacy geometry across 4096 heterogeneous pages");
            check(visible, $"indexed {mode} viewport includes exactly the visible pages");
            check(hits, $"indexed {mode} hit testing matches full-layout reference");
            check(nearest, $"indexed {mode} active-page selection matches full-layout reference");
            var changed = pages.Select(p => p with { Annotations = [new Annotation()] }).ToArray();
            check(!index.Update(changed, mode) && index.BuildCount == 1, $"{mode} annotation-only edits do not rebuild geometry");
        }
        var empty = new PageLayoutIndex([]); check(empty.TotalHeight(1) == 0 && !empty.Visible(800, 600, 1, 0, 0).Any(), "empty layout is well-defined");
        reject(() => new PageLayoutIndex([new PdfPageState { Width = double.NaN }]), "index rejects nonfinite dimensions");
        reject(() => new PageLayoutIndex(pages).Place(-1, 1000, 1), "index rejects invalid page number");
        reject(() => new PageLayoutIndex(pages).TotalHeight(double.NaN), "index rejects nonfinite zoom");
        var test = new PageLayoutIndex(pages); var iterations = 1000; var sum = 0d;
        // Warm both implementations before allocation and timing measurements.
        for (var i = 0; i < 20; i++) { sum += PageLayout.Arrange(pages, 1440, .88, i * 1000, 0)[0].Bounds.X; sum += test.Place(20, 1440, .88).Bounds.X; }
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) sum += PageLayout.Arrange(pages, 1440, .88, i * 1000, 0).Count(p => p.Bounds.Intersects(new(0, 0, 1440, 960)));
        timer.Stop(); var oldMs = timer.Elapsed.TotalMilliseconds; var oldBytes = GC.GetAllocatedBytesForCurrentThread()-bytes;
        bytes = GC.GetAllocatedBytesForCurrentThread(); timer.Restart();
        for (var i = 0; i < iterations; i++) sum += test.Visible(1440, 960, .88, i * 1000, 0, overscan: 0).Count();
        timer.Stop(); var newMs = timer.Elapsed.TotalMilliseconds; var newBytes = GC.GetAllocatedBytesForCurrentThread()-bytes;
        check(newBytes < oldBytes / 10 && test.BuildCount == 1, "indexed scrolling allocates less than one tenth of full-layout traversal");
        Directory.CreateDirectory("artifacts/structured");
        var report = new { pages=pages.Length, iterations, legacyMilliseconds=oldMs, indexedMilliseconds=newMs, legacyBytes=oldBytes, indexedBytes=newBytes, checksum=sum };
        File.WriteAllText("artifacts/structured/layout-performance.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"LAYOUT PERF: {oldMs:F2} ms / {oldBytes:N0} B -> {newMs:F2} ms / {newBytes:N0} B ({iterations} queries, {pages.Length} pages)");
    }
}
