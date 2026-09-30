using System.Diagnostics;
using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.PageLabels.Benchmarks;

// Same valid values and Unicode prefix in both implementations. Only formatting
// is timed; the labels, delegates, and result lists are constructed beforehand.
var current = Enumerable.Range(1, 4096).Select(number => new PdfPageLabel
    { Style = PdfPageLabelStyle.RomanLower, Prefix = "Front-章-", Number = number }).ToArray();
var previous = current.Select(label => new PreviousPageLabel
    { Style = label.Style, Prefix = label.Prefix, Number = label.Number }).ToArray();
if (!current.Select(label => label.Format()).SequenceEqual(previous.Select(label => label.Format())))
    throw new InvalidDataException("Formatter outputs differ.");
Func<int, string> oldFormat = i => previous[i].Format(), newFormat = i => current[i].Format();
int MeasureWork(Func<int, string> format)
{
    var checksum = 0;
    for (var i = 0; i < current.Length; i++)
    {
        var value = format(i);
        checksum = unchecked(checksum * 31 + value.Length + value[^1]);
    }
    return checksum;
}
for (var warmup = 0; warmup < 3; warmup++) { MeasureWork(oldFormat); MeasureWork(newFormat); }
var samples = new List<object>();
for (var sample = 0; sample < 9; sample++)
foreach (var updated in sample % 2 == 0 ? new[] { false, true } : new[] { true, false })
{
    var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
    var checksum = MeasureWork(updated ? newFormat : oldFormat);
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    samples.Add(new { sample, mode = updated ? "direct-result-string" : "previous-symbol-array-builder", elapsedMilliseconds = elapsed, currentThreadManagedBytes = bytes, checksum });
}
Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), labels = current.Length,
    style = "lowercase Roman", prefix = "Front-章-", samples,
    scope = "Formatting only, including validation and result-string allocations. Same valid inputs and exact output equivalence; three warmups, nine alternating-order samples. Excludes input/index construction, PDF parsing/writing, UI, native/GPU allocations and total-process memory. No timing pass threshold." }, new JsonSerializerOptions { WriteIndented = true }));
