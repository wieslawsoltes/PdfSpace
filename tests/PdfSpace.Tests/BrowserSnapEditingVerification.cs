using PdfSpace.Core;
using PdfSpace.Pdf;

internal static class BrowserSnapEditingVerification
{
    public static void Run(string directory)
    {
        PdfPageObject[] Read(string name) => PdfObjectEditor.Read(PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, name)), name), 0);
        var checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidDataException(label); checks++; Console.WriteLine("PASS " + label); }
        var resized = Read("snap-browser-resized.pdf");
        Check(resized.Length == 3 && resized[0].Bounds == new RectD(50, 100, 250, 60), "browser resize guides write exact native edge alignment");
        Check(resized[1].Bounds == new RectD(300, 250, 80, 90), "browser snapped resize leaves target path untouched");
        var aspect = Read("snap-browser-aspect.pdf");
        Check(Math.Abs(aspect[0].Bounds.Width - 250) < .02 && Math.Abs(aspect[0].Bounds.Height - 150) < .02,
            "browser Shift resize retains aspect while snapping native edge");
        var points = Read("snap-browser-node.pdf");
        var node = points[2].LocalToPage.Transform(new(points[2].Nodes[0].Values[0], points[2].Nodes[0].Values[1]));
        Check(Math.Abs(node.X - 300) < .02 && Math.Abs(node.Y - 450) < .02, "browser snapped Bezier anchor persists as native operands with locked Y");
        Check(points[0].Bounds == new RectD(50, 100, 100, 60) && points[1].Bounds == resized[1].Bounds,
            "browser vector-point snapping preserves other objects");
        Console.WriteLine($"{checks} browser snap editing verification checks passed.");
    }
}
