using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Pdf;
using SkiaSharp;
using var typeface = SKTypeface.FromFamilyName("DejaVu Sans");
var blank = new PdfWorkspace { Pages = Enumerable.Range(0, 8).Select(_ => new PdfPageState()).ToArray() };
var document = PdfPageMarks.Apply(blank, Enumerable.Range(0, 8).ToArray(), new PdfPageMarkSettings(), typeface).Workspace;
var combined = document;
for (var i = 1; i < 8; i++) combined = WorkspaceComposition.Append(combined, document);
int[] selected = [63];
IReadOnlyList<PdfPageMarkInfo> Read(bool scoped) => scoped ? PdfPageMarks.Read(combined, selected) : PdfPageMarks.Read(combined);
for (var i = 0; i < 3; i++) { Read(false); Read(true); }
var results = new List<object>();
for (var i = 0; i < 9; i++)
foreach (var scoped in i % 2 == 0 ? new[] { false, true } : new[] { true, false })
{
    var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
    var records = Read(scoped);
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    if (records.Count != (scoped ? 1 : 64) || !records.All(r => r.Intact)) throw new Exception("Invalid fixture");
    results.Add(new { sample = i, mode = scoped ? "selected-page" : "all-referenced-pages", elapsedMilliseconds = elapsed, managedBytes = bytes });
}
Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), sourceCount = combined.Sources.Length, pages = 64, samples = results,
    scope = "Same 0.6 engine: full mark inspection (previous inspector behavior) versus current-page inspection. Eight source aliases with distinct IDs, each eight blank marked pages. Three warmups; nine alternating-order samples. Includes parsing/checksums, excludes UI, writing, GPU/native allocation and index construction. Not an application-wide speedup." }, new JsonSerializerOptions { WriteIndented = true }));
