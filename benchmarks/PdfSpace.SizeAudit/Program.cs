using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Pdf;

if (args.Length != 1) throw new ArgumentException("Provide one PDF file to audit.");
var bytes = File.ReadAllBytes(args[0]);
var cache = new PdfSizeAuditCache();
var expected = PdfSizeAudit.Read(bytes); var cached = cache.Read(bytes);
if (expected.FileBytes != cached.FileBytes || expected.EncodedStreamBytes != cached.EncodedStreamBytes ||
    !expected.Categories.SequenceEqual(cached.Categories) || !expected.LargestStreams.SequenceEqual(cached.LargestStreams))
    throw new InvalidDataException("Cached and uncached audit differ.");
const int iterations = 100;
object Measure(bool reuse, int sample)
{
    var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
    for (var i = 0; i < iterations; i++) { if (reuse) cache.Read(bytes); else PdfSizeAudit.Read(bytes); }
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    return new { sample, mode = reuse ? "cached-source" : "reparse-source", iterations, elapsedMilliseconds = elapsed, managedBytes = allocated };
}
for (var i = 0; i < 3; i++) { Measure(false, -1); Measure(true, -1); }
var samples = new List<object>();
for (var i = 0; i < 9; i++)
    foreach (var reuse in i % 2 == 0 ? new[] { false, true } : new[] { true, false }) samples.Add(Measure(reuse, i));
Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = Environment.Version.ToString(), inputBytes = bytes.Length, expected.IndirectObjectCount, expected.StreamCount,
    samples, scope = "Same immutable source and exact equivalent results; three warmups, nine alternating-order batches. " +
        "Cold path includes PDF parsing and stream classification. Cached path is identity lookup. Both exclude input IO, workspace aggregation, UI, native/GPU memory. Not an application-wide speed comparison."
}, new JsonSerializerOptions { WriteIndented = true }));
