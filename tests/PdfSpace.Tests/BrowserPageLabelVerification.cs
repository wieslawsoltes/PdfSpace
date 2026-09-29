using PdfSpace.Core;
using PdfSpace.Pdf;
internal static class BrowserPageLabelVerification
{
    public static void Run(string directory)
    {
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException(name); Console.WriteLine("PASS " + name); }
        PdfWorkspace Read(string name) => PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, name)), name);
        var sections = Read("labels-browser-sections.pdf"); var labels = new PageLabelIndex(sections.Pages);
        Check(labels[0] == "i" && labels[1] == "ii" && labels[2] == "Chapter-1" && labels[5] == "Chapter-4", "browser writes native mixed Roman and chapter labels");
        var reset = Read("labels-browser-reset.pdf");
        Check(reset.Pages.All(p => p.Label is null), "browser physical label reset removes the native label tree");
        Check(PdfSpace.Documents.PdfReader.ExtractText(sections).Contains("Good ideas"), "navigation labels retain searchable source content");
        using var renderer = new PdfSpace.Skia.PdfRenderer();
        Check(renderer.ExportPng(sections, 0, 1).SequenceEqual(renderer.ExportPng(reset, 0, 1)), "label reset changes no rendered source pixels");
        Console.WriteLine("4 browser page-label verification checks passed.");
    }
}
