using System.Text;
using PdfSpace.Pdf;
using PdfSpace.Skia;

internal static class BrowserAttachmentEditingVerification
{
    public static void Run(string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); }
        byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(directory, "attachment-edit-browser-" + name + ".pdf"));
        var originalBytes = Encoding.UTF8.GetBytes("Native attachment — original.\n");
        foreach (var name in new[] { "added", "described", "replaced", "blank" })
        {
            var bytes = Bytes(name); var entry = PdfEmbeddedFiles.Read(bytes).Single();
            Check(entry.FileName == "attachment-edit-input.txt", "browser attachment preserves its original catalog filename: " + name);
            Check(entry.Description == (name is "added" or "blank" ? "Initial — Żółć" : "Reviewed — 日本語"), "browser stores actual Unicode attachment descriptions: " + name);
            Check(PdfEmbeddedFiles.Extract(bytes, entry).SequenceEqual(name == "replaced" ? "Replacement payload\n"u8.ToArray() : originalBytes), "browser authored attachment payload is exact: " + name);
        }
        Check(PdfEmbeddedFiles.Read(Bytes("removed")).Count == 0, "browser attachment removal persists in the exported native catalog");
        using var renderer = new PdfRenderer();
        var original = NativeObjectSample.Create();
        foreach (var name in new[] { "added", "described", "replaced", "removed" })
        {
            var edited = PdfDocumentEngine.Open(Bytes(name), "edited.pdf");
            Check(edited.Pages.Length == original.Pages.Length, "browser attachment authoring leaves page count unchanged: " + name);
            for (var page = 0; page < original.Pages.Length; page++)
                Check(renderer.ExportPng(original, page).SequenceEqual(renderer.ExportPng(edited, page)), "browser attachment authoring preserves native page pixels: " + name + " page " + page);
        }
    }
}
