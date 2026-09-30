using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Core;
using PdfSpace.Editing;
using PdfSpace.Pdf;

internal static class SizeAuditTests
{
    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var bytes = Fixture(); var hash = SHA256.HashData(bytes);
        var report = PdfSizeAudit.Read(bytes);

        check(SHA256.HashData(bytes).SequenceEqual(hash), "space audit leaves original bytes untouched");
        check(report.FileBytes == bytes.Length && report.StreamCount == 27, "audit counts streams once despite repeated references");
        long Count(PdfStreamKind kind) => report.Categories.Single(c => c.Kind == kind).StreamCount;
        check(Count(PdfStreamKind.PageContent) == 1 && Count(PdfStreamKind.Image) == 2, "audit distinguishes shared page content and image masks");
        check(Count(PdfStreamKind.FontProgram) == 1 && Count(PdfStreamKind.FormAppearance) == 1, "audit classifies embedded font and Form streams");
        check(Count(PdfStreamKind.Metadata) == 2 && Count(PdfStreamKind.EmbeddedFile) == 1, "audit identifies metadata and embedded files without decoding");
        check(report.Categories.Sum(c => c.EncodedBytes) == report.EncodedStreamBytes, "audit categories partition the stream subtotal exactly");
        check(Math.Abs(report.Categories.Sum(c => c.PercentOfStreamBytes) - 100) < 1e-9, "audit percentages use encoded stream bytes as denominator");
        check(report.LargestStreams.Count == 12, "audit top-stream output stays bounded");
        check(report.LargestStreams.Select(s => s.EncodedBytes).SequenceEqual(report.LargestStreams.Select(s => s.EncodedBytes).OrderDescending()), "audit top streams are ordered by payload size");
        check(report.LargestStreams.All(s => s.ObjectNumber > 0), "audit object identifiers are native PDF references");
        using (var native = PdfDocumentEngine.OpenNative(bytes))
        {
            var expected = native.Internals.GetAllObjects().OfType<PdfDictionary>().Where(d => d.Stream is not null)
                .OrderByDescending(d => d.Stream.Length).ThenBy(d => d.Reference!.ObjectNumber).Take(12)
                .Select(d => d.Reference!.ObjectNumber);
            check(expected.SequenceEqual(report.LargestStreams.Select(s => s.ObjectNumber)), "bounded audit heap matches exhaustive size sorting including ties");
        }
        var cache = new PdfSizeAuditCache(2);
        var a = cache.Read(bytes); var b = cache.Read(bytes);
        check(ReferenceEquals(a,b) && cache.SourceParseCount == 1 && cache.CacheHitCount == 1, "repeated audit reuses immutable report without source parsing");
        for (var warmup = 0; warmup < 3; warmup++) MeasureCacheHits(cache, bytes);
        check(MeasureCacheHits(cache, bytes) == 0, "100000 warmed source-audit cache hits allocate zero managed bytes");
        var source = new PdfSource(Guid.NewGuid(), "private-title.pdf", bytes);
        var workspace = new PdfWorkspace { Sources = [source, source with { Id = Guid.NewGuid() }], Pages = [new() { SourceId = source.Id }] };
        var aggregate = cache.Analyze(workspace);
        check(aggregate.SourceEntries == 2 && aggregate.UniqueSourceBuffers == 1 && aggregate.Sources[0].Aliases == 2, "workspace audit deduplicates reference-identical source aliases");
        check(aggregate.RetainedFileBytes == bytes.Length, "aliased PDF bytes are not charged twice");
        var session = new EditorSession(workspace); var identity = session.Document;
        cache.Analyze(session.Document);
        check(ReferenceEquals(identity, session.Document) && !session.IsDirty && session.UndoCount == 0, "audit adds no undo entries or dirty state");
        var parses = cache.SourceParseCount;
        cache.Analyze(PdfPageLabels.Apply(workspace, 0, 1, new PdfPageLabel()));
        check(cache.SourceParseCount == parses, "page-label metadata changes reuse source-audit cache");
        var changed = bytes.ToArray(); cache.Read(changed);
        check(cache.SourceParseCount == parses + 1, "new buffer identity is re-audited even with identical bytes");
        cache.Read(bytes); cache.Read(bytes.ToArray());
        check(cache.CachedSourceCount == 2 && ReferenceEquals(cache.Read(bytes), a), "audit cache bounds reports and keeps most recently used source");
        cache.Read(changed);
        check(cache.SourceParseCount == parses + 3, "least recently used audit source is reparsed after eviction");
        var cold = new PdfSizeAuditCache();
        var weak = RememberTemporary(cold, bytes);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!weak.IsAlive, "audit cache cannot keep discarded PDF buffers alive");
        cold.Clear(); check(cold.CachedSourceCount == 0, "explicit audit cache clear releases report entries");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        reject(() => cache.Read(bytes, canceled.Token), "audit observes cancellation on a cached hit");
        reject(() => PdfSizeAudit.Read(bytes, canceled.Token), "audit observes cancellation before native parsing");
        reject(() => cache.Analyze(workspace, canceled.Token), "workspace audit observes cancellation before source traversal");
        reject(() => PdfSizeAudit.Read(null!), "audit rejects null bytes");
        reject(() => PdfSizeAudit.Read("not pdf"u8.ToArray()), "audit rejects non-PDF bytes");
        reject(() => new PdfSizeAuditCache(0), "audit cache rejects invalid capacity");
        var empty = cache.Analyze(new PdfWorkspace());
        check(empty.UniqueSourceBuffers == 0 && empty.EncodedStreamBytes == 0 && empty.Categories.All(c => c.PercentOfStreamBytes == 0), "blank workspace has a finite zero-source report");
        var json = aggregate.ToJson(); using var parsed = JsonDocument.Parse(json);
        check(parsed.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "audit JSON is source-generated and versioned");
        check(!Encoding.UTF8.GetString(json).Contains("private-title"), "audit report omits file names and document text");
        reject(() => ((IList<PdfStreamUsage>)aggregate.Categories)[0] = aggregate.Categories[1], "audit result categories cannot be externally mutated");
        reject(() => ((IList<PdfStreamObjectUsage>)a.LargestStreams)[0] = a.LargestStreams[1], "cached audit largest-stream entries are immutable");
        SizeAuditBoundaryTests.Run(check, reject);
        var clock = Stopwatch.GetTimestamp();
        for (var i = 0; i < 100_000; i++) cache.Read(changed);
        var elapsed = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        Directory.CreateDirectory("artifacts/structured"); Directory.CreateDirectory("artifacts/engine");
        File.WriteAllBytes("artifacts/structured/audit-fixture.pdf", bytes);
        File.WriteAllBytes("artifacts/structured/audit-fixture.json", aggregate.ToJson());
        File.WriteAllText("artifacts/structured/audit-cache-performance.json", JsonSerializer.Serialize(new
        { runtime = Environment.Version.ToString(), iterations = 100_000, milliseconds = elapsed,
          scope = "Warm source-identity cache hits only; excludes parsing, report aggregation, serialization, UI and native memory." }));
        var sample = NativeObjectSample.Create();
        File.WriteAllBytes("artifacts/engine/audit-sample.pdf", sample.Sources[0].Bytes);
        File.WriteAllBytes("artifacts/engine/audit-sample.json", new PdfSizeAuditCache().Analyze(sample).ToJson());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureCacheHits(PdfSizeAuditCache cache, byte[] bytes)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100_000; i++) cache.Read(bytes);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RememberTemporary(PdfSizeAuditCache cache, byte[] template)
    { var temporary = template.ToArray(); cache.Read(temporary); return new WeakReference(temporary); }

    private static byte[] Fixture()
    {
        using var d = new PdfDocument(); d.Options.CompressContentStreams = false;
        var root = new PdfArray(d); d.Internals.Catalog.Elements["/AuditFixture"] = root;
        PdfDictionary Stream(int length, string? type = null, string? subtype = null)
        {
            var stream = new PdfDictionary(d); d.Internals.AddObject(stream);
            if (type is not null) stream.Elements.SetName("/Type", type);
            if (subtype is not null) stream.Elements.SetName("/Subtype", subtype);
            stream.CreateStream(Enumerable.Repeat((byte)' ',length).ToArray()); root.Elements.Add(stream.Reference!);
            return stream;
        }
        var content = Stream(8); content.Stream.Value = "q\nQ\nq\nQ\n"u8.ToArray();
        for (var i=0;i<2;i++)
        { var page = d.AddPage(); page.Elements["/Contents"] = new PdfArray(d, content.Reference!, content.Reference!); }
        var image = Stream(19, "/XObject", "/Image"); image.Elements.SetName("/Filter", "/DCTDecode"); // Deliberately not a JPEG: audit must not decode it.
        var mask = Stream(17, "/XObject", "/Image"); image.Elements["/SMask"] = mask.Reference!;
        var font = Stream(333);
        var descriptor = new PdfDictionary(d); d.Internals.AddObject(descriptor); descriptor.Elements.SetName("/Type", "/FontDescriptor");
        descriptor.Elements["/FontFile2"] = font.Reference!; root.Elements.Add(descriptor.Reference!);
        Stream(24, "/XObject", "/Form"); Stream(27, "/EmbeddedFile"); Stream(33, "/Metadata");
        for (var i = 0; i < 19; i++) Stream((i + 1) * 10);
        return PdfDocumentEngine.Bytes(d);
    }
}
