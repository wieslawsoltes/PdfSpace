using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Layout;

internal static class SpatialBoundsIndexTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var result = new List<int> { 7 };
        check(SpatialBoundsIndex.Empty.Query(new(0,0,1,1), result) == 0 && result.Count == 0 && SpatialBoundsIndex.Empty.HitTest(new(0,0)) == -1,
            "empty spatial queries clear reused output and return no hit");
        RectD[] overlap = [new(1,1,10,10), new(1,1,10,10), new(5,5,0,0)];
        var stack = new SpatialBoundsIndex(overlap); overlap[2] = new(100,100,1,1);
        check(stack.HitTest(new(5,5)) == 2 && stack.HitTest(new(2,2)) == 1, "topmost paint order and zero-size bounds survive index reordering and input mutation");
        check(stack.HitTest(new(0,0),1) == 1, "spatial hit tolerance includes exact inflated boundary");
        stack.Query(new(5,5,0,0), result); check(result.SequenceEqual([0,1,2]), "spatial area queries retain ascending original paint order");
        stack.Query(new(4,4,2,2), result, true); check(result.SequenceEqual([2]), "contained selection differs from intersecting selection");
        reject(() => new SpatialBoundsIndex([new(0,0,-1,2)]), "negative index dimensions rejected");
        reject(() => new SpatialBoundsIndex([new(double.NaN,0,1,1)]), "nonfinite index bounds rejected");
        reject(() => stack.HitTest(new(0,0),-1), "negative hit tolerance rejected");
        reject(() => stack.Query(new(0,0,double.PositiveInfinity,1), result), "nonfinite query rejected");
        var random = new Random(74191);
        var bounds = Enumerable.Range(0, 12000).Select(i => new RectD(random.Next(-1000,1000), random.Next(-1000,1000), i % 17 == 0 ? 0 : random.Next(1,55), i % 13 == 0 ? 0 : random.Next(1,65))).ToArray();
        var index = new SpatialBoundsIndex(bounds);
        var hitsEqual = true; var areasEqual = true; var containedEqual = true;
        for (var iteration = 0; iteration < 400; iteration++)
        {
            var point = new PointD(random.Next(-1100,1100), random.Next(-1100,1100)); var tolerance = iteration % 7;
            var expected = Array.FindLastIndex(bounds, b => b.Inflate(tolerance).Contains(point));
            hitsEqual &= index.HitTest(point,tolerance) == expected;
            var area = new RectD(point.X, point.Y, iteration % 101, iteration % 79);
            index.Query(area, result); areasEqual &= result.SequenceEqual(Enumerable.Range(0,bounds.Length).Where(i=>area.Intersects(bounds[i])));
            index.Query(area, result, true); containedEqual &= result.SequenceEqual(Enumerable.Range(0,bounds.Length).Where(i=>area.Contains(new(bounds[i].X,bounds[i].Y)) && area.Contains(new(bounds[i].Right,bounds[i].Bottom))));
        }
        check(hitsEqual, "BVH topmost hit equals linear reference across 400 heterogeneous queries");
        check(areasEqual && containedEqual, "BVH marquee queries equal inclusive linear references");
        var grid = new SpatialBoundsIndex(Enumerable.Range(0,16384).Select(i=>new RectD((i%128)*20,(i/128)*20,8,8)).ToArray());
        result.EnsureCapacity(64);
        for (var i=0;i<2000;i++) { grid.HitTest(new(21,21)); grid.Query(new(20,20,2,2),result); }
        const int count=5000; var start = GC.GetAllocatedBytesForCurrentThread(); var timer=Stopwatch.StartNew(); long tested=0;
        for(var i=0;i<count;i++)
        {
            var p=new PointD((i%128)*20+1,((i/128)%128)*20+1);
            grid.HitTest(p,0,out var hitTests); tested+=hitTests;
            tested+=grid.Query(new(p.X,p.Y,1,1),result);
        }
        timer.Stop(); var allocated=GC.GetAllocatedBytesForCurrentThread()-start;
        check(tested < (long)count*16384/100, "dense spatial queries test less than one percent of linear bounds");
        check(allocated < 4096, "warmed spatial queries reuse buffers without per-query allocation");
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllText("artifacts/structured/object-spatial-performance.json",JsonSerializer.Serialize(new { objects=16384, iterations=count, queries=count*2, testedBounds=tested, linearBoundTests=(long)count*2*16384, elapsedMilliseconds=timer.Elapsed.TotalMilliseconds, currentThreadManagedBytes=allocated, scope="Synthetic grid point/intersection microbenchmark; not frame rate, native allocation or general complexity guarantee." },new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"SPATIAL PERF: {count*2} queries, {tested} tested bounds, {allocated} B, {timer.Elapsed.TotalMilliseconds:F2} ms");
    }
}
