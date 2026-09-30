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
        var special = Read("labels-browser-literal.pdf"); var exact = new PageLabelIndex(special.Pages);
        Check(Enumerable.Range(0, 6).Select(i => exact[i]).SequenceEqual(new[] { "#Part-1", "#Part-2", "#2", "#Part-4", "#Part-5", "=Appendix" }),
            "browser preserves hash and equals prefixes in native labels");
        Check(exact.Resolve("#2", out var physical) == PageLabelMatch.Found && physical == 1 &&
            exact.Resolve("=#2", out var literal) == PageLabelMatch.Found && literal == 2,
            "native reopen retains distinct physical and literal hash navigation");
        Check(exact.Resolve("==Appendix", out var appendix) == PageLabelMatch.Found && appendix == 5,
            "native reopen retains literal equals navigation");
        Check(renderer.ExportPng(sections, 0, 1).SequenceEqual(renderer.ExportPng(special, 0, 1)),
            "command-looking labels do not change rendered source content");
        Console.WriteLine("8 browser page-label verification checks passed.");
    }
}
