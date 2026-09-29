using System.Text.Json;
using System.Runtime.CompilerServices;
using PdfSpace.Core;
using PdfSpace.Pdf;
using PdfSpace.Workbench;

internal static class ObjectDiagnosticsTests
{
    public static void Run(Action<bool, string> check)
    {
        var cache = new PdfObjectDiagnostics();
        var descriptor = new PdfPageObject(Guid.NewGuid(), Guid.NewGuid(), 0, "hash", "", 0, 1,
            PdfPageObjectKind.Text, new(10, 20, 30, 40), PdfAffineMatrix.Identity, "fingerprint", true, "")
        { Text = "Żółć \"café\" \\ native\ntext" };
        PdfPageObject[] objects = [descriptor];
        string Read(PdfPageObject[] items, bool redact = false)
        {
            using var bytes = new MemoryStream();
            using (var json = new Utf8JsonWriter(bytes)) cache.Write(json, items, redact);
            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }
        var plain = Read(objects);
        check(JsonDocument.Parse(plain).RootElement[0].GetProperty("text").GetString() == descriptor.Text,
            "diagnostic cache preserves native Unicode and JSON escaping");
        check(Read(objects) == plain && cache.BuildCount == 1 && cache.CachedBytes > 0,
            "unchanged descriptor observations reuse one serialization");
        var redacted = Read(objects, true);
        check(JsonDocument.Parse(redacted).RootElement[0].GetProperty("text").GetString() == "[protected]" && cache.BuildCount == 2,
            "sensitivity changes invalidate serialized diagnostic text");
        check(Read(objects) == plain && cache.BuildCount == 3,
            "diagnostic sensitivity can change back without stale values");
        var replacement = Read([descriptor with { Bounds = new(50, 60, 70, 80) }]);
        check(JsonDocument.Parse(replacement).RootElement[0].GetProperty("x").GetDouble() == 50 && cache.BuildCount == 4,
            "new descriptor identity invalidates cached geometry");
        cache.Clear(); check(cache.CachedBytes == 0, "detached inspector releases serialized diagnostic bytes");
        check(Read([]) == "[]", "empty descriptor cache emits a valid complete array");
        var source = PopulateCollectible(cache, descriptor);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        check(!source.IsAlive, "diagnostic cache cannot root old object descriptor arrays");
        var dense = Enumerable.Range(0, 4096).Select(i => descriptor with { Bounds = new(i, i, 10, 10) }).ToArray();
        var expected = Read(dense); var builds = cache.BuildCount;
        var reused = true;
        for (var i = 0; i < 100; i++) reused &= Read(dense) == expected && cache.BuildCount == builds;
        check(reused, "100 dense immutable observations reuse descriptor serialization");
        GC.KeepAlive(cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PopulateCollectible(PdfObjectDiagnostics cache, PdfPageObject descriptor)
    {
        var source = new[] { descriptor };
        using var buffer = new MemoryStream();
        using var json = new Utf8JsonWriter(buffer); cache.Write(json, source, false);
        return new WeakReference(source);
    }
}
