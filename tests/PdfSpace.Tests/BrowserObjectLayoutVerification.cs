using PdfSpace.Core;
using PdfSpace.Pdf;

internal static class BrowserObjectLayoutVerification
{
    internal static void Run(string directory)
    {
        PdfPageObject[] Read(string name) => PdfObjectEditor.Read(PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, name)), name), 0)
            .Where(p => p.Kind == PdfPageObjectKind.Path).ToArray();
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidDataException(label); checks++; Console.WriteLine("PASS " + label); }
        static bool Near(double a, double b) => Math.Abs(a - b) < .02;
        var original = PdfObjectEditor.Read(ObjectEditingSample.Create(), 0).Where(p => p.Kind == PdfPageObjectKind.Path).ToArray();
        var spaced = Read("layout-browser-spacing.pdf");
        Check(Near(spaced[1].Bounds.X, 189) && Near(spaced[1].Bounds.X - spaced[0].Bounds.Right, spaced[2].Bounds.X - spaced[1].Bounds.Right), "browser equal gaps persist as native object transforms");
        Check(spaced[0].Bounds == original[0].Bounds && spaced[2].Bounds == original[2].Bounds, "browser spacing keeps both outer objects fixed");
        var aligned = Read("layout-browser-reference.pdf");
        Check(aligned.Take(3).All(p => Near(p.Bounds.X, 140)), "browser uses the explicit second reference, not the leftmost object");
        Check(aligned[1].Bounds == original[1].Bounds, "browser reference object remains fixed");
        var sized = Read("layout-browser-size.pdf");
        Check(sized.Take(3).All(p => Near(p.Bounds.Width, 130) && Near(p.Bounds.Height, 70)), "browser size matching retains native bounds after reopen");
        Check(sized.Take(3).Select((p, i) => p.Bounds.Center.Distance(aligned[i].Bounds.Center) < .02).All(v => v), "browser size matching preserves centers");
        var page = Read("layout-browser-page.pdf");
        Check(Near(page[0].Bounds.Bottom, 842) && Near(page[0].Bounds.X, 48), "browser single object page-bottom alignment persists");
        Check(page.Skip(1).Select(p => p.Bounds).SequenceEqual(original.Skip(1).Select(p => p.Bounds)), "browser page alignment leaves other native paths untouched");
        Console.WriteLine($"{checks} browser object layout verification checks passed.");
    }
}
