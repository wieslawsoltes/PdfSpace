using System.Diagnostics;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Pdf;
using SkiaSharp;

internal static class PageMarkPerformanceTests
{
    public static void Run(Action<bool, string> check)
    {
        using var font = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        const int count = 8;
        var original = new PdfWorkspace { Pages = Enumerable.Range(0, count).Select(_ => new PdfPageState()).ToArray() };
        var settings = new PdfPageMarkSettings { HeaderLeft = "Shared page-mark font", FooterCenter = "Batch reference" };
        var indices = Enumerable.Range(0, count).ToArray();
        PdfWorkspace Apply(bool batch)
        {
            if (batch) return PdfPageMarks.Apply(original, indices, settings, font).Workspace;
            var current = original;
            foreach (var page in indices) current = PdfPageMarks.Apply(current, [page], settings, font).Workspace;
            return current;
        }
        Apply(true); Apply(false); // Warm both paths, including native font loading.
        var measurements = new List<object>();
        for (var sample = 0; sample < 3; sample++)
        foreach (var batch in sample % 2 == 0 ? new[] { true, false } : new[] { false, true })
        {
            var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
            var workspace = Apply(batch); var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            using var native = PdfDocumentEngine.OpenNative(workspace.Sources[0].Bytes);
            var fonts = native.Internals.GetAllObjects().OfType<PdfDictionary>().Count(d => d.Elements.GetName("/Subtype") == "/Type0");
            check(fonts == (batch ? 1 : count), "mark performance fixture validates serialized shared font count");
            check(PdfPageMarks.Read(workspace).Count == count, "both benchmark paths mark every requested page");
            measurements.Add(new { sample, path = batch ? "one-batch" : "per-page-apply", elapsedMilliseconds = elapsed, managedBytes = allocated, pdfBytes = workspace.Sources[0].Bytes.Length, fonts });
        }
        Directory.CreateDirectory("artifacts/structured");
        File.WriteAllText("artifacts/structured/page-mark-performance.json", JsonSerializer.Serialize(new {
            runtime = Environment.Version.ToString(), pages = count, measurements,
            scope = "Identical text on eight blank pages via Apply(batch) versus repeated Apply(page). One warmup and three alternating-order samples. Includes native writing and rehydration, excludes UI/GPU; allocations are current-thread managed bytes, not process memory. No timing threshold." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
