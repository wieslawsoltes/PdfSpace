using PdfSpace.Pdf;
using PdfSpace.Core;
using PdfSpace.Documents;
using SkiaSharp;

internal static class BrowserPageMarkVerification
{
    public static void Run(string directory)
    {
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); Console.WriteLine("PASS " + message); count++; }
        PdfWorkspace Open(string name) => PdfDocumentEngine.Open(File.ReadAllBytes(Path.Combine(directory, "marks-browser-" + name + ".pdf")), name + ".pdf");
        var numbered = Open("headers"); var marks = PdfPageMarks.Read(numbered);
        Check(marks.Count == 2 && marks.All(m => m.Intact), "browser native headers retain verified ownership");
        var text = PdfReader.ExtractText(numbered);
        Check(text.Contains("Żółć café") && text.Contains("Page 1 of 2") && text.Contains("Page 2 of 2"), "browser header exports contain searchable Unicode and page numbers");
        Check(marks.Select(m => m.TypefaceFingerprint).Distinct().Count() == 1, "browser batch headers use the same font identity");
        var updated = Open("updated");
        Check(PdfReader.ExtractText(updated).Contains("Final review") && !PdfReader.ExtractText(updated).Contains("Żółć café"), "browser updating removes the previous native header text");
        Check(PdfPageMarks.Read(updated).Count == 2, "browser header update does not accumulate owned layers");
        var removed = Open("removed");
        Check(PdfPageMarks.Read(removed).Count == 0 && !PdfReader.ExtractText(removed).Contains("Final review"), "browser removal drops actual owned text and its manifest");
        var watermarked = Open("watermark"); var water = PdfPageMarks.Read(watermarked).Single();
        Check(water.PageIndex == 1 && water.Settings.Kind == PdfPageMarkKind.Watermark && water.Intact, "browser watermark affects only the chosen native page");
        Check(water.Settings.Rotation == -35 && Math.Abs(water.Settings.Opacity - .3) < .00001, "browser watermark angle and opacity persist");
        using (var reader = new PdfTextReader(watermarked))
        {
            Check(!string.Join(' ', reader.Words(watermarked.Pages[0]).Select(w => w.Text)).Contains("CONFIDENTIAL"), "unselected page gains no watermark text");
            Check(string.Join(' ', reader.Words(watermarked.Pages[1]).Select(w => w.Text)).Contains("CONFIDENTIAL"), "diagonal watermark stays searchable");
        }
        var bates = Open("bates"); var records = PdfPageMarks.Read(bates);
        Check(records.Count == 2 && records.All(m => m.Intact), "browser Bates settings reopen intact");
        var batesText = PdfReader.ExtractText(bates);
        Check(batesText.Contains("CASE-000042") && batesText.Contains("CASE-000043"), "browser Bates sequence retains prefix, padding and starting value");
        Console.WriteLine($"{count} browser page-mark checks passed.");
    }
}
