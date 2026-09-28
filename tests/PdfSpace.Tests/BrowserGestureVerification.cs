using PdfSpace.Pdf;

internal static class BrowserGestureVerification
{
    public static void Run(string directory)
    {
        var path = Path.Combine(directory, "constrained-browser-resize.pdf");
        var document = PdfDocumentEngine.Open(File.ReadAllBytes(path), "constrained.pdf");
        var objects = PdfObjectEditor.Read(document,0);
        var resized = objects.First(o => o.Kind == PdfPageObjectKind.Path).Bounds;
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException(name); Console.WriteLine("PASS " + name); }
        Check(Math.Abs(resized.Width-182)<1 && Math.Abs(resized.Height-98)<1,"browser Shift+Alt gesture writes proportional native geometry");
        Check(Math.Abs(resized.Center.X-113)<.02 && Math.Abs(resized.Center.Y-397)<.02,"browser center-based gesture retains the PDF object center");
        Check(Math.Abs(objects.Where(o=>o.Kind==PdfPageObjectKind.Path).Skip(1).First().Bounds.Width-130)<.01,"browser constrained resize leaves sibling path unchanged");
        Check(PdfObjectEditor.Read(document,1).Count(o=>o.Kind==PdfPageObjectKind.Image)==1,"browser constrained resize leaves shared-resource page intact");
        Console.WriteLine("4 browser gesture verification checks passed.");
    }
}
