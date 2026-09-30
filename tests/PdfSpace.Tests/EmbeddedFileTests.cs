using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSpace.Pdf;

internal static class EmbeddedFileTests
{
    internal static readonly byte[] Plain = Encoding.UTF8.GetBytes("PdfSpace attachment\nNo external requests.\n");
    internal static readonly byte[] Unicode = Encoding.UTF8.GetBytes("Zażółć gęślą jaźń — embedded Unicode.\n");
    internal static readonly byte[] Html = Encoding.UTF8.GetBytes("<!doctype html><title>Not executed</title>");

    internal static byte[] Fixture(int count = 3)
    {
        using var d = new PdfDocument(); d.AddPage();
        var tree = new PdfDictionary(d); d.Internals.AddObject(tree);
        PdfObjects.DictionaryValue(d.Internals.Catalog, "/Names").Elements["/EmbeddedFiles"] = tree.Reference!;
        var pairs = new PdfArray(d); tree.Elements["/Names"] = pairs;
        for (var i = 0; i < count; i++)
        {
            var payload = i == 1 ? Unicode : i == 2 ? Html : Plain;
            var embedded = new PdfDictionary(d); d.Internals.AddObject(embedded);
            embedded.Elements.SetName("/Type", "/EmbeddedFile");
            embedded.Elements.SetName("/Subtype", "/application/octet-stream");
            embedded.CreateStream(i == 1 ? Compress(payload) : payload.ToArray());
            if (i == 1) embedded.Elements.SetName("/Filter", "/FlateDecode");
            PdfObjects.DictionaryValue(embedded, "/Params").Elements.SetInteger("/Size", payload.Length);
            var spec = new PdfDictionary(d); d.Internals.AddObject(spec); spec.Elements.SetName("/Type", "/Filespec");
            spec.Elements["/UF"] = new PdfString(i == 1 ? "raport-Żółć.txt" : i == 2 ? "../../preview.html" : $"report-{i + 1}.txt", PdfStringEncoding.Unicode);
            spec.Elements.SetString("/Desc", "Original embedded document");
            PdfObjects.DictionaryValue(spec, "/EF").Elements["/UF"] = embedded.Reference!;
            pairs.Elements.Add(new PdfString($"key-{i + 1:D3}")); pairs.Elements.Add(spec.Reference!);
        }
        return PdfDocumentEngine.Bytes(d);
    }
    private static byte[] Compress(byte[] input)
    { using var output = new MemoryStream(); using (var z = new ZLibStream(output, CompressionLevel.SmallestSize, true)) z.Write(input); return output.ToArray(); }
    private static PdfDictionary Tree(PdfDocument d) => PdfObjects.Dictionary(PdfObjects.Dictionary(d.Internals.Catalog.Elements["/Names"])!.Elements["/EmbeddedFiles"])!;
    private static PdfArray Pairs(PdfDocument d) => PdfObjects.Array(Tree(d).Elements["/Names"])!;
    private static PdfDictionary Spec(PdfDocument d, int index = 0) => PdfObjects.Dictionary(Pairs(d).Elements[2 * index + 1])!;
    private static PdfDictionary Payload(PdfDocument d, int index = 0) => PdfObjects.Dictionary(PdfObjects.Dictionary(Spec(d, index).Elements["/EF"])!.Elements["/UF"])!;
    private static byte[] Changed(byte[] original, Action<PdfDocument> change)
    { using var d = PdfDocumentEngine.OpenNative(original); change(d);
      foreach (var item in d.Internals.GetAllObjects().OfType<PdfDictionary>())
          if (item.Stream is { } stream) item.Elements.SetInteger("/Length", stream.Length);
      return PdfDocumentEngine.Bytes(d); }
    private static byte[] ExtractFirst(byte[] bytes) => PdfEmbeddedFiles.Extract(bytes, PdfEmbeddedFiles.Read(bytes)[0]);

    public static void Run(Action<bool, string> check, Action<Action, string> reject)
    {
        var bytes = Fixture(); var hash = SHA256.HashData(bytes); var files = PdfEmbeddedFiles.Read(bytes);
        check(files.Count == 3 && files.All(f => f.CanExtract), "attachment catalog lists three files without decoding them");
        check(files[1].FileName == "raport-Żółć.txt", "attachment Unicode file name survives native serialization");
        check(files[2].DownloadName == "preview.html.download", "embedded active-content filename is neutralized for download");
        check(PdfEmbeddedFiles.Extract(bytes, files[0]).SequenceEqual(Plain), "unfiltered attachment extracts exact bytes");
        check(PdfEmbeddedFiles.Extract(bytes, files[1]).SequenceEqual(Unicode), "Flate attachment extracts exact Unicode bytes");
        check(PdfEmbeddedFiles.Extract(bytes, files[2]).SequenceEqual(Html), "attachment download does not execute or alter active payload");
        check(SHA256.HashData(bytes).SequenceEqual(hash), "attachment reads and extraction never mutate source bytes");
        reject(() => ((IList<PdfEmbeddedFileInfo>)files)[0] = files[1], "attachment metadata collection cannot be mutated");
        reject(() => PdfEmbeddedFiles.Extract(bytes, files[0] with { DownloadName = "changed" }), "forged attachment descriptor is rejected");
        reject(() => PdfEmbeddedFiles.Extract(Fixture(1), files[0]), "attachment selection cannot target changed source bytes");
        reject(() => PdfEmbeddedFiles.Extract(bytes, files[0] with { Key = "missing" }), "missing attachment key is rejected");
        using (var empty = new PdfDocument()) { empty.AddPage(); check(PdfEmbeddedFiles.Read(PdfDocumentEngine.Bytes(empty)).Count == 0, "unattached PDF has an empty attachment catalog"); }
        check(PdfEmbeddedFiles.Read(Fixture(65)).Count == 65, "attachment reader reaches entries beyond the first UI page");
        check(PdfEmbeddedFiles.Read(Fixture(512)).Count == 512, "attachment reader accepts its exact 512-entry limit");
        reject(() => PdfEmbeddedFiles.Read(Fixture(513)), "attachment reader rejects excessive name pairs");
        var nested = Changed(bytes, d =>
        {
            var root = Tree(d); var values = Pairs(d); root.Elements.Remove("/Names");
            var first = new PdfDictionary(d); var last = new PdfDictionary(d); d.Internals.AddObject(first); d.Internals.AddObject(last);
            first.Elements["/Names"] = new PdfArray(d, values.Elements[0], values.Elements[1]);
            first.Elements["/Limits"] = new PdfArray(d, values.Elements[0], values.Elements[0]);
            last.Elements["/Names"] = new PdfArray(d, values.Elements[2], values.Elements[3], values.Elements[4], values.Elements[5]);
            last.Elements["/Limits"] = new PdfArray(d, values.Elements[2], values.Elements[4]);
            root.Elements["/Kids"] = new PdfArray(d, first.Reference!, last.Reference!);
        });
        check(PdfEmbeddedFiles.Read(nested).Select(f => f.Key).SequenceEqual(files.Select(f => f.Key)), "nested attachment name trees retain every ordered key");
        check(PdfEmbeddedFiles.Extract(nested, PdfEmbeddedFiles.Read(nested)[1]).SequenceEqual(Unicode), "nested attachment payload survives extraction");
        reject(() => PdfEmbeddedFiles.Read(Changed(bytes, d => Pairs(d).Elements[2] = Pairs(d).Elements[0])), "duplicate decoded attachment keys reject ambiguous selection");
        reject(() => PdfEmbeddedFiles.Read(Changed(bytes, d => Pairs(d).Elements.RemoveAt(0))), "odd name pairs reject malformed catalogs");
        reject(() => PdfEmbeddedFiles.Read(Changed(bytes, d => Tree(d).Elements["/Kids"] = new PdfArray(d, Tree(d).Reference!))), "ambiguous Names plus Kids is rejected");
        reject(() => PdfEmbeddedFiles.Read(Changed(bytes, d => { var t = Tree(d); t.Elements.Remove("/Names"); t.Elements["/Kids"] = new PdfArray(d, t.Reference!); })), "cyclic attachment trees terminate explicitly");
        reject(() => PdfEmbeddedFiles.Read(Changed(nested, d => PdfObjects.Array(Tree(d).Elements["/Kids"])!.Elements.Add(PdfObjects.Array(Tree(d).Elements["/Kids"])!.Elements[0]))), "shared attachment tree children cannot double-count files");
        reject(() => PdfEmbeddedFiles.Read(Changed(bytes, d => Tree(d).Elements["/Limits"] = new PdfArray(d, new PdfString("wrong"), new PdfString("key-003")))), "attachment Limits mismatch is rejected");
        reject(() => PdfEmbeddedFiles.Read(Changed(bytes, d => Spec(d).Elements.SetString("/Desc", new string('x', 4097)))), "attachment descriptions are bounded");
        var unsupported = Changed(bytes, d => Payload(d).Elements.SetName("/Filter", "/RunLengthDecode"));
        check(!PdfEmbeddedFiles.Read(unsupported)[0].CanExtract, "unsupported attachment filters remain visible without decoding");
        reject(() => ExtractFirst(unsupported), "unsupported attachment codec cannot be extracted silently");
        var external = Changed(bytes, d => { Spec(d).Elements.SetName("/FS", "/URL"); Spec(d).Elements.SetString("/F", "https://example.invalid/private"); });
        check(!PdfEmbeddedFiles.Read(external)[0].CanExtract, "URL file specifications never trigger external resolution");
        var missing = Changed(bytes, d => Spec(d).Elements.Remove("/EF"));
        check(!PdfEmbeddedFiles.Read(missing)[0].CanExtract, "external-only attachments are visible but unavailable");
        var mismatch = Changed(bytes, d => PdfObjects.Dictionary(Payload(d).Elements["/Params"])!.Elements.SetInteger("/Size", 1));
        reject(() => ExtractFirst(mismatch), "declared attachment size cannot hide an incomplete or excessive payload");
        var predictor = Changed(bytes, d => PdfObjects.DictionaryValue(Payload(d, 1), "/DecodeParms").Elements.SetInteger("/Predictor", 12));
        check(!PdfEmbeddedFiles.Read(predictor)[1].CanExtract, "unsupported Flate predictor is explicitly blocked");
        var arrayFilter = Changed(bytes, d => Payload(d, 1).Elements["/Filter"] = new PdfArray(d, new PdfName("/FlateDecode")));
        check(PdfEmbeddedFiles.Extract(arrayFilter, PdfEmbeddedFiles.Read(arrayFilter)[1]).SequenceEqual(Unicode), "single-element native Flate filter arrays are supported");
        var corrupt = Changed(bytes, d => { var p = Payload(d, 1); var raw = p.Stream.Value.ToArray(); raw[^1] ^= 1; p.Stream.Value = raw; });
        reject(() => PdfEmbeddedFiles.Extract(corrupt, PdfEmbeddedFiles.Read(corrupt)[1]), "corrupt attachment zlib checksum rejects partial output");
        var truncated = Changed(bytes, d => { var p = Payload(d, 1); p.Stream.Value = p.Stream.Value[..^3]; p.Elements.Remove("/Params"); });
        reject(() => PdfEmbeddedFiles.Extract(truncated, PdfEmbeddedFiles.Read(truncated)[1]), "truncated zlib stream is rejected even without declared size");
        var bomb = Changed(bytes, d => { var p = Payload(d); p.Stream.Value = Compress(new byte[PdfEmbeddedFiles.MaximumExtractedBytes + 1]); p.Elements.SetName("/Filter", "/FlateDecode"); p.Elements.Remove("/Params"); });
        reject(() => ExtractFirst(bomb), "compressed attachment cannot expand beyond the output byte budget");
        var tooLarge = Changed(bytes, d => PdfObjects.Dictionary(Payload(d).Elements["/Params"])!.Elements.SetInteger("/Size", PdfEmbeddedFiles.MaximumExtractedBytes + 1));
        check(!PdfEmbeddedFiles.Read(tooLarge)[0].CanExtract, "declared over-limit attachment is blocked before decoding");
        var emptyPayload = Changed(bytes, d => { var p = Payload(d); p.Stream.Value = []; p.Elements.Remove("/Params"); });
        check(ExtractFirst(emptyPayload).Length == 0, "zero-byte embedded files are supported");
        foreach (var (input, expected) in new[] { ("../../file.txt", "file.txt"), (@"C:\temp\report.txt", "report.txt"), ("NUL.txt", "attachment-NUL.txt"), ("COM¹.txt", "attachment-COM¹.txt"), ("evil\u202Etxt.exe", "evil_txt.exe.download"), ("..", "attachment.bin") })
            check(PdfEmbeddedFiles.DownloadFileName(input) == expected, "attachment safe filename: " + expected);
        check(PdfEmbeddedFiles.DownloadFileName(new string('x', 200)).Length == 120, "download filename length is bounded");
        var cache = new PdfEmbeddedFileCache(2); var firstReport = cache.Read(bytes);
        check(ReferenceEquals(cache.Read(bytes), firstReport) && cache.SourceParseCount == 1, "repeat attachment inspection reuses immutable metadata");
        for (var i = 0; i < 3; i++) Warm(cache, bytes);
        var begin = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.GetTimestamp(); Warm(cache, bytes);
        var elapsed = Stopwatch.GetElapsedTime(clock).TotalMilliseconds; var allocation = GC.GetAllocatedBytesForCurrentThread() - begin;
        check(allocation == 0, "100000 warm attachment lookups allocate zero managed bytes");
        var copy = bytes.ToArray(); cache.Read(copy); cache.Read(bytes); cache.Read(bytes.ToArray());
        check(cache.Count == 2 && ReferenceEquals(cache.Read(bytes), firstReport), "attachment cache capacity retains most recent source");
        cache.Read(copy); check(cache.SourceParseCount == 4, "attachment cache reparses evicted buffer identities");
        reject(() => cache.Read("invalid"u8.ToArray()), "failed attachment parse is not cached");
        check(cache.SourceParseCount == 4 && cache.Count == 2, "failed attachment parse cannot evict valid metadata");
        var weakCache = new PdfEmbeddedFileCache(); var weak = Temporary(weakCache, bytes);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); check(!weak.IsAlive, "attachment metadata cache never roots discarded PDF buffers");
        weakCache.Clear(); check(weakCache.Count == 0, "attachment metadata cache can be explicitly cleared");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        reject(() => PdfEmbeddedFiles.Read(bytes, cancel.Token), "attachment inspection observes precancellation");
        reject(() => PdfEmbeddedFiles.Extract(bytes, files[0], cancel.Token), "attachment extraction observes precancellation");
        var hits = cache.CacheHitCount; reject(() => cache.Read(bytes, cancel.Token), "cached attachment reads observe cancellation");
        check(cache.CacheHitCount == hits, "cancelled attachment reads do not change cache telemetry");
        reject(() => new PdfEmbeddedFileCache(0), "attachment cache rejects zero capacity");
        reject(() => new PdfEmbeddedFileCache(33), "attachment cache rejects excessive capacity");
        Directory.CreateDirectory("artifacts/engine");
        File.WriteAllBytes("artifacts/engine/attachments-sample.pdf", bytes);
        File.WriteAllBytes("artifacts/engine/attachments-many.pdf", Fixture(65));
        File.WriteAllBytes("artifacts/engine/attachment-plain.txt", Plain);
        File.WriteAllBytes("artifacts/engine/attachment-unicode.txt", Unicode);
        File.WriteAllText("artifacts/engine/attachment-cache-performance.json", JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), queries = 100000, elapsedMilliseconds = elapsed, managedBytes = allocation, scope = "Warm source-identity metadata cache hits only; excludes parsing, hashing, decoding, UI, JSON and native/GPU memory." }));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Warm(PdfEmbeddedFileCache cache, byte[] source) { for (var i = 0; i < 100000; i++) cache.Read(source); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Temporary(PdfEmbeddedFileCache cache, byte[] bytes) { var copy = bytes.ToArray(); cache.Read(copy); return new(copy); }
}
