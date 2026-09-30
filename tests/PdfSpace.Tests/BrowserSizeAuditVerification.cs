using System.Text.Json;
using PdfSpace.Core;
using PdfSpace.Pdf;

internal static class BrowserSizeAuditVerification
{
    public static void Run(string directory)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidDataException(name); Console.WriteLine("PASS " + name); }
        using var expected = JsonDocument.Parse(new PdfSizeAuditCache().Analyze(NativeObjectSample.Create()).ToJson());
        using var downloaded = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "audit-browser-report.json")));
        // The fixture's PDF metadata differs per generation, but stream structure and
        // payload lengths are deterministic; no filenames or generated IDs occur here.
        Check(JsonElement.DeepEquals(expected.RootElement, downloaded.RootElement), "browser audit JSON matches native source classification and payload lengths");
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "audit-browser-metadata.json")));
        Check(JsonElement.DeepEquals(metadata.RootElement, downloaded.RootElement), "metadata-only edits retain identical source audit values");
        using var empty = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "audit-browser-empty.json")));
        using var zero = JsonDocument.Parse(new PdfSizeAuditCache().Analyze(new PdfWorkspace()).ToJson());
        Check(JsonElement.DeepEquals(empty.RootElement, zero.RootElement), "empty document exports its own zero-source report");
        Console.WriteLine("3 browser space audit verification checks passed.");
    }
}
