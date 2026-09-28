using PdfSpace.Pdf;

internal static class BrowserManipulationVerification
{
    public static void Run(string directory)
    {
        var passed = 0;
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidDataException(label);
            passed++; Console.WriteLine("PASS " + label);
        }
        var rotated = PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, "manipulation-browser-rotated.pdf")), "rotated.pdf");
        var paths = PdfObjectEditor.Read(rotated, 0).Where(o => o.Kind == PdfPageObjectKind.Path).ToArray();
        var b = paths[0].Bounds;
        Check(Math.Abs(b.Width - 200 / Math.Sqrt(2)) < 1 && Math.Abs(b.Height - 200 / Math.Sqrt(2)) < 1,
            "browser rotation handle writes native rotated geometry, not an overlay");
        Check(Math.Abs(b.Center.X - 113) < .02 && Math.Abs(b.Center.Y - 397) < .02, "browser free rotation retains its pivot");
        Check(Math.Abs(paths[1].Bounds.Width - 130) < .02 && Math.Abs(paths[1].Bounds.X - 140) < .02, "browser rotation leaves sibling paths unchanged");
        var snapped = PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, "manipulation-browser-snapped.pdf")), "snapped.pdf");
        var moved = PdfObjectEditor.Read(snapped, 0).First(o => o.Kind == PdfPageObjectKind.Path).Bounds;
        Check(Math.Abs(moved.X - 140) < .02 && Math.Abs(moved.Y - 362) < .02, "browser snapped move persists precise alignment and locked axis");
        Check(PdfObjectEditor.Read(snapped, 1).Count(o => o.Kind == PdfPageObjectKind.Image) == 1, "browser manipulation leaves the shared-resource page intact");
        Console.WriteLine($"{passed} browser manipulation verification checks passed.");
    }
}
