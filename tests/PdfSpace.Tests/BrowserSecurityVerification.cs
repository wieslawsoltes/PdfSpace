using PdfSharp.Pdf.IO;
using PdfSpace.Pdf;
using NativeReader = PdfSharp.Pdf.IO.PdfReader;

internal static class BrowserSecurityVerification
{
    public static void Run(string directory)
    {
        var bytes = File.ReadAllBytes(Path.Combine(directory, "protected.pdf"));
        foreach (var password in new[] { "", "wrong-password", "Read-password-2026" })
        {
            var rejected = false;
            try { using var invalid = NativeReader.Open(new MemoryStream(bytes), password, PdfDocumentOpenMode.Modify); }
            catch (PdfReaderException) { rejected = true; }
            if (!rejected) throw new Exception("Browser output granted editing access without its owner password.");
        }
        using var document = NativeReader.Open(new MemoryStream(bytes), "Owner-password-2026", PdfDocumentOpenMode.Modify);
        if (document.PageCount != 6 || document.SecurityHandler.Elements.GetInteger("/V") != 5 || document.SecurityHandler.Elements.GetInteger("/R") != 6 || document.SecurityHandler.Elements.GetInteger("/Length") != 256)
            throw new Exception("Browser output is not the expected six-page AES-256 revision-6 PDF.");
        if (document.SecuritySettings.PermitPrint || document.SecuritySettings.PermitFullQualityPrint || document.SecuritySettings.PermitExtractContent || document.SecuritySettings.PermitModifyDocument)
            throw new Exception("Requested PDF permission flags were not retained.");
        var provider = new NativePdfSecurityProvider();
        var unlocked = provider.UnlockAsync(bytes, "Owner-password-2026").GetAwaiter().GetResult();
        if (!unlocked.WasEncrypted || PdfDocumentEngine.Open(unlocked.Bytes, "unlocked.pdf").Pages.Length != 6)
            throw new Exception("The independent native backend could not reopen the browser-encrypted PDF.");
        Console.WriteLine("PASS independent PDFsharp validation of browser QPDF AES-256 output, permission flags and owner authentication");
    }
}
