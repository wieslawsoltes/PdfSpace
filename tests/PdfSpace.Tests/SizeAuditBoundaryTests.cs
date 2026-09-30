using PdfSharp.Pdf;
using PdfSpace.Pdf;

internal static class SizeAuditBoundaryTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        using var document = new PdfDocument(); document.AddPage();
        var keep = new PdfArray(document); document.Internals.Catalog.Elements["/AuditTestObjects"] = keep;
        PdfDictionary Stream(int bytes)
        {
            var item = new PdfDictionary(document); document.Internals.AddObject(item);
            item.CreateStream(new byte[bytes]); keep.Elements.Add(item.Reference!); return item;
        }
        var firstFont = Stream(23); var secondFont = Stream(29);
        foreach (var key in new[] { "/FontFile", "/FontFile2", "/FontFile3" })
        {
            var font = new PdfDictionary(document); document.Internals.AddObject(font);
            font.Elements.SetName("/Type", "/Font");
            var directDescriptor = new PdfDictionary(document);
            directDescriptor.Elements.SetName("/Type", "/FontDescriptor");
            directDescriptor.Elements[key] = firstFont.Reference!;
            font.Elements["/FontDescriptor"] = directDescriptor;
            keep.Elements.Add(font.Reference!);
        }
        var descriptor = new PdfDictionary(document); document.Internals.AddObject(descriptor);
        descriptor.Elements.SetName("/Type", "/FontDescriptor"); descriptor.Elements["/FontFile3"] = secondFont.Reference!;
        keep.Elements.Add(descriptor.Reference!);
        for (var i = 0; i < 20; i++) Stream(4096);
        var bytes = PdfDocumentEngine.Bytes(document);
        var cache = new PdfSizeAuditCache(1); var report = cache.Read(bytes);
        var programs = report.Categories.Single(c => c.Kind == PdfStreamKind.FontProgram);
        check(programs.StreamCount == 2 && programs.EncodedBytes == 52, "direct and indirect font descriptors count shared programs exactly once");
        check(report.LargestStreams.Count == 12 && report.LargestStreams.All(s => s.EncodedBytes == 4096), "top-k selection truncates equal-size streams without dropping larger payloads");
        using var parsed = PdfDocumentEngine.OpenNative(bytes);
        var expected = parsed.Internals.GetAllObjects().OfType<PdfDictionary>().Where(d => d.Stream?.Length == 4096)
            .Select(d => d.Reference!.ObjectNumber).Order().Take(12);
        check(report.LargestStreams.Select(s => s.ObjectNumber).SequenceEqual(expected), "tied top-k streams retain the twelve lowest native object identifiers");
        reject(() => cache.Read("not a PDF"u8.ToArray()), "failed audit parse rejects instead of caching a partial report");
        check(cache.SourceParseCount == 1 && cache.CachedSourceCount == 1 && ReferenceEquals(cache.Read(bytes), report), "failed parse cannot evict the last valid report");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var hits = cache.CacheHitCount;
        reject(() => cache.Read(bytes, cancellation.Token), "cancelled cache access rejects before changing hit telemetry");
        check(cache.CacheHitCount == hits && cache.SourceParseCount == 1, "cancelled reads leave cache telemetry unchanged");
        reject(() => new PdfSizeAuditCache(33), "audit cache rejects excessive report capacity");
    }
}
